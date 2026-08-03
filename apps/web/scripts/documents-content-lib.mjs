import { mkdir, readFile, readdir, stat, writeFile } from "node:fs/promises";
import { dirname, relative, resolve, sep } from "node:path";
import matter from "gray-matter";

export const DOCUMENT_LOCALES = ["en", "ru"];

const CONTENT_FILE_PATTERN = /\.(?:md|mdx)$/iu;
const LOCALE_SUFFIX_PATTERN = /\.(en|ru)(\.(?:md|mdx))$/iu;
const DOCUMENT_STATUSES = new Set(["draft", "review", "published", "archived"]);
const PRODUCTION_STATUSES = new Set(["published", "archived"]);
const ALLOWED_MDX_COMPONENTS = new Set([
  "Callout",
  "Steps",
  "Step",
  "Files",
  "Folder",
  "File",
  "Tabs",
  "Tab",
  "DocumentLinkGrid",
  "DocumentLinkGroup",
  "DocumentLinkCard",
]);
const MARKDOWN_FENCE_PATTERN = /^\s*(`{3,}|~{3,})/u;
const MARKDOWN_LINK_PATTERN =
  /(?<!!)\[[^\]]+\]\((?:<([^>\s]+)>|([^\s)]+))(?:\s+(?:"[^"]*"|'[^']*'|\([^)]*\)))?\)/gu;
const MARKDOWN_IMAGE_PATTERN =
  /!\[[^\]]*\]\((?:<([^>\s]+)>|([^\s)]+))(?:\s+(?:"[^"]*"|'[^']*'|\([^)]*\)))?\)/gu;
const MARKDOWN_REFERENCE_DEFINITION_PATTERN =
  /^\s*\[[^\]]+\]:\s*(?:<([^>\s]+)>|([^<\s]+))(?:\s+["'(][^"')]*["')])?\s*$/u;
const MDX_HREF_PATTERN =
  /\bhref=(?:"([^"]+)"|'([^']+)'|\{`([^`]+)`\}|\{"([^"]+)"\}|\{'([^']+)'\})/gu;
const MDX_SRC_PATTERN =
  /\bsrc=(?:"([^"]+)"|'([^']+)'|\{`([^`]+)`\}|\{"([^"]+)"\}|\{'([^']+)'\})/gu;
const MDX_COMPONENT_PATTERN = /<\/?([A-Z][^\s/>]*)/gu;
const METADATA_FIELDS = new Set([
  "title",
  "description",
  "group",
  "groupOrder",
  "parentItem",
  "parentItemOrder",
  "order",
  "status",
  "hide",
  "toc",
  "purpose",
  "author",
  "version",
  "editedAt",
  "reading",
  "source",
]);

const compareText = (left, right) => (left < right ? -1 : left > right ? 1 : 0);
const normalizePath = (path) => path.split(sep).join("/");
const isPlainObject = (value) =>
  value !== null && typeof value === "object" && !Array.isArray(value);
const isNonEmptyString = (value) =>
  typeof value === "string" && value.trim().length > 0;
const isFiniteNumber = (value) =>
  typeof value === "number" && Number.isFinite(value);

function fail(code, message) {
  throw new Error(`${code}: ${message}`);
}

function getUnfencedLines(content) {
  const lines = content.split(/\r?\n/u);
  const visible = [];
  let activeFence;

  for (const [index, line] of lines.entries()) {
    const fence = MARKDOWN_FENCE_PATTERN.exec(line)?.[1];
    if (fence) {
      const candidate = { marker: fence[0], length: fence.length };
      if (
        !activeFence ||
        (candidate.marker === activeFence.marker &&
          candidate.length >= activeFence.length)
      ) {
        activeFence = activeFence ? undefined : candidate;
        continue;
      }
    }

    if (!activeFence) {
      visible.push({ line, number: index + 1 });
    }
  }

  return visible;
}

const stripInlineCode = (line) =>
  line.replace(/(`+)(?:[^`]|`(?!\1))*?\1/gu, "");

const stripHeadingMarkdown = (value) =>
  value
    .replace(/\s*\{#[^}]+\}\s*$/gu, "")
    .replace(/!\[([^\]]*)\]\([^)]+\)/gu, "$1")
    .replace(/\[([^\]]+)\]\([^)]+\)/gu, "$1")
    .replace(/\[([^\]]+)\]\[[^\]]*\]/gu, "$1")
    .replace(/[*_`~]/gu, "")
    .replace(/<[^>]+>/gu, " ")
    .replace(/\s+/gu, " ")
    .trim();

const slugifyHeading = (title) =>
  title
    .trim()
    .toLowerCase()
    .replace(/ё/gu, "е")
    .replace(/[^\p{Letter}\p{Number}\s-]/gu, "")
    .replace(/\s+/gu, "-")
    .replace(/-+/gu, "-")
    .replace(/^-|-$/gu, "");

export function extractHeadings(content) {
  const headings = [];
  const seenIds = new Map();

  for (const { line } of getUnfencedLines(content)) {
    const match = /^(#{2,3})[ \t]+(.+?)[ \t]*#*[ \t]*$/u.exec(line);
    if (!match) continue;

    const title = stripHeadingMarkdown(match[2]);
    if (!title) continue;

    const baseId = slugifyHeading(title) || "section";
    const count = seenIds.get(baseId) ?? 0;
    seenIds.set(baseId, count + 1);
    headings.push({
      level: match[1].length,
      title,
      id: count === 0 ? baseId : `${baseId}-${count + 1}`,
    });
  }

  return headings;
}

function validateMdxSyntax(content, sourcePath) {
  for (const { line, number } of getUnfencedLines(content)) {
    const visibleLine = stripInlineCode(line);
    if (
      /^\s*import(?:\s+(?:[A-Za-z_$*{]|["'])|\s*\()/u.test(visibleLine) ||
      /^\s*export(?:\s+(?:default\b|const\b|let\b|var\b|function\b|class\b|async\b|type\b|interface\b|enum\b|namespace\b|[{:*=])|\s*=)/u.test(
        visibleLine,
      )
    ) {
      fail(
        "documents_mdx_module_syntax",
        `${sourcePath}:${number} contains forbidden import/export syntax`,
      );
    }

    MDX_COMPONENT_PATTERN.lastIndex = 0;
    let match;
    while ((match = MDX_COMPONENT_PATTERN.exec(visibleLine)) !== null) {
      if (!ALLOWED_MDX_COMPONENTS.has(match[1])) {
        fail(
          "documents_unknown_mdx_component",
          `${sourcePath}:${number} uses ${match[1]}`,
        );
      }
    }
  }
}

function matchHref(match) {
  return match.slice(1).find((value) => value !== undefined);
}

function extractContentTargets(content) {
  const targets = [];

  for (const { line, number } of getUnfencedLines(content)) {
    const visibleLine = stripInlineCode(line);

    for (const [kind, pattern] of [
      ["image", MARKDOWN_IMAGE_PATTERN],
      ["image", MDX_SRC_PATTERN],
      ["link", MARKDOWN_LINK_PATTERN],
      ["link", MDX_HREF_PATTERN],
    ]) {
      pattern.lastIndex = 0;
      let match;
      while ((match = pattern.exec(visibleLine)) !== null) {
        const href = matchHref(match);
        if (href) targets.push({ kind, href, line: number });
      }
    }

    const reference = MARKDOWN_REFERENCE_DEFINITION_PATTERN.exec(visibleLine);
    const href = reference && matchHref(reference);
    if (href) {
      targets.push({
        kind: href.trim().startsWith("/img/") ? "image" : "link",
        href,
        line: number,
      });
    }
  }

  return targets;
}

function safeDecode(value, decoder) {
  try {
    return decoder(value);
  } catch {
    return value;
  }
}

function normalizeDocumentHref(href, currentUrl) {
  const trimmed = href.trim();
  if (!trimmed || /^https?:\/\//iu.test(trimmed)) {
    return undefined;
  }
  if (trimmed.startsWith("#")) {
    return {
      targetUrl: currentUrl,
      fragment: safeDecode(trimmed.slice(1), decodeURIComponent),
    };
  }

  const hashIndex = trimmed.indexOf("#");
  const withoutHash = hashIndex < 0 ? trimmed : trimmed.slice(0, hashIndex);
  const rawFragment = hashIndex < 0 ? undefined : trimmed.slice(hashIndex + 1);
  const pathname = safeDecode(withoutHash.split("?", 1)[0], decodeURI).replace(
    /\/+$/u,
    "",
  );

  if (pathname !== "/docs" && !pathname.startsWith("/docs/")) {
    return undefined;
  }

  const rawTarget =
    pathname === "/docs"
      ? "index"
      : pathname.slice("/docs/".length).replace(/\/index$/u, "");

  return {
    targetUrl: rawTarget || "index",
    fragment:
      rawFragment === undefined
        ? undefined
        : safeDecode(rawFragment, decodeURIComponent),
  };
}

function normalizeImageHref(href) {
  const trimmed = href.trim();
  if (!trimmed.startsWith("/img/")) return undefined;
  const pathname = trimmed.split("#", 1)[0].split("?", 1)[0];
  return safeDecode(pathname, decodeURI);
}

const isProductionVisible = (document) =>
  PRODUCTION_STATUSES.has(document.meta.status) && document.meta.hide !== true;

function validatePublishedLocales(documents) {
  const byUrl = new Map();
  for (const document of documents) {
    const variants = byUrl.get(document.canonicalUrl) ?? [];
    variants.push(document);
    byUrl.set(document.canonicalUrl, variants);
  }

  for (const [canonicalUrl, variants] of byUrl) {
    if (!variants.some(isProductionVisible)) continue;

    const visibleLocales = new Set(
      variants
        .filter(isProductionVisible)
        .map((document) => document.contentLocale),
    );
    const missing = DOCUMENT_LOCALES.filter(
      (locale) => !visibleLocales.has(locale),
    );
    if (missing.length > 0) {
      fail(
        "documents_missing_published_locale",
        `${canonicalUrl} is missing production-visible ${missing.join(", ")}`,
      );
    }
  }
}

async function validateContentTargets(documents, sourceByPath, publicRoot) {
  const allUrls = new Set(documents.map((document) => document.canonicalUrl));
  const documentsByUrlAndLocale = new Map(
    documents.map((document) => [
      `${document.canonicalUrl}\u0000${document.contentLocale}`,
      document,
    ]),
  );
  const firstDocumentByUrl = new Map();
  for (const document of documents) {
    if (!firstDocumentByUrl.has(document.canonicalUrl)) {
      firstDocumentByUrl.set(document.canonicalUrl, document);
    }
  }

  for (const document of documents) {
    for (const target of extractContentTargets(
      sourceByPath.get(document.sourcePath) ?? "",
    )) {
      if (target.kind === "link") {
        const normalized = normalizeDocumentHref(
          target.href,
          document.canonicalUrl,
        );
        if (!normalized) continue;
        if (!allUrls.has(normalized.targetUrl)) {
          fail(
            "documents_broken_link",
            `${document.sourcePath}:${target.line} -> ${target.href}`,
          );
        }

        const matchingLocaleTarget = documentsByUrlAndLocale.get(
          `${normalized.targetUrl}\u0000${document.contentLocale}`,
        );
        if (
          isProductionVisible(document) &&
          (!matchingLocaleTarget || !isProductionVisible(matchingLocaleTarget))
        ) {
          fail(
            "documents_unpublished_link",
            `${document.sourcePath}:${target.line} -> ${target.href}`,
          );
        }

        if (normalized.fragment) {
          const targetDocument =
            documentsByUrlAndLocale.get(
              `${normalized.targetUrl}\u0000${document.contentLocale}`,
            ) ?? firstDocumentByUrl.get(normalized.targetUrl);
          if (
            !targetDocument.headings.some(
              (heading) => heading.id === normalized.fragment,
            )
          ) {
            fail(
              "documents_broken_fragment",
              `${document.sourcePath}:${target.line} -> ${target.href}`,
            );
          }
        }
        continue;
      }

      const imageHref = normalizeImageHref(target.href);
      if (!imageHref) continue;
      const absolutePublicRoot = resolve(publicRoot);
      const imagePath = resolve(absolutePublicRoot, imageHref.slice(1));
      const imageRelativePath = relative(absolutePublicRoot, imagePath);
      let imageStat;
      if (
        imageRelativePath.startsWith(`..${sep}`) ||
        imageRelativePath === ".."
      ) {
        fail(
          "documents_missing_image",
          `${document.sourcePath}:${target.line} -> ${target.href}`,
        );
      }
      try {
        imageStat = await stat(imagePath);
      } catch (error) {
        if (error && typeof error === "object" && error.code === "ENOENT") {
          fail(
            "documents_missing_image",
            `${document.sourcePath}:${target.line} -> ${target.href}`,
          );
        }
        throw error;
      }
      if (!imageStat.isFile()) {
        fail(
          "documents_missing_image",
          `${document.sourcePath}:${target.line} -> ${target.href}`,
        );
      }
    }
  }
}

async function findContentFiles(directory) {
  let children;

  try {
    children = await readdir(directory, { withFileTypes: true });
  } catch (error) {
    if (error && typeof error === "object" && error.code === "ENOENT") {
      return [];
    }
    throw error;
  }

  const files = [];
  for (const child of children.sort((left, right) =>
    compareText(left.name, right.name),
  )) {
    const path = resolve(directory, child.name);
    if (child.isDirectory()) {
      files.push(...(await findContentFiles(path)));
    } else if (child.isFile() && CONTENT_FILE_PATTERN.test(child.name)) {
      files.push(path);
    }
  }

  return files;
}

function parseContentPath(contentRoot, path) {
  const sourcePath = normalizePath(relative(contentRoot, path));
  const localeMatch = sourcePath.match(LOCALE_SUFFIX_PATTERN);
  if (!localeMatch) {
    fail(
      "documents_missing_locale_suffix",
      `${sourcePath} must include an explicit .en or .ru suffix`,
    );
  }
  const contentLocale = localeMatch[1].toLowerCase();
  const canonicalSourcePath = sourcePath.replace(LOCALE_SUFFIX_PATTERN, "$2");
  const withoutExtension = canonicalSourcePath.replace(
    CONTENT_FILE_PATTERN,
    "",
  );
  const canonicalUrl =
    withoutExtension === "index"
      ? "index"
      : withoutExtension.replace(/\/index$/u, "");

  return {
    sourcePath,
    canonicalSourcePath,
    canonicalUrl,
    contentLocale,
    hasExplicitLocale: true,
  };
}

function validateIsoDate(value, sourcePath) {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/u.test(value)) {
    fail(
      "documents_metadata_invalid_edited_at",
      `${sourcePath} must use YYYY-MM-DD`,
    );
  }

  const [year, month, day] = value.split("-").map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  if (
    date.getUTCFullYear() !== year ||
    date.getUTCMonth() !== month - 1 ||
    date.getUTCDate() !== day
  ) {
    fail(
      "documents_metadata_invalid_edited_at",
      `${sourcePath} has an invalid calendar date`,
    );
  }
}

function validateMetadata(data, sourcePath) {
  if (!isPlainObject(data)) {
    fail(
      "documents_metadata_invalid",
      `${sourcePath} frontmatter must be an object`,
    );
  }

  for (const field of Object.keys(data)) {
    if (!METADATA_FIELDS.has(field)) {
      fail(
        "documents_metadata_unknown_field",
        `${sourcePath} contains ${field}`,
      );
    }
  }

  for (const field of ["title", "description", "group", "parentItem"]) {
    if (!isNonEmptyString(data[field])) {
      fail(
        "documents_metadata_invalid",
        `${sourcePath} requires a non-empty ${field}`,
      );
    }
  }
  for (const field of ["order"]) {
    if (!isFiniteNumber(data[field])) {
      fail(
        "documents_metadata_invalid",
        `${sourcePath} requires a finite ${field}`,
      );
    }
  }
  if (typeof data.toc !== "boolean") {
    fail("documents_metadata_invalid", `${sourcePath} requires boolean toc`);
  }
  if (typeof data.status !== "string" || !DOCUMENT_STATUSES.has(data.status)) {
    fail("documents_metadata_invalid", `${sourcePath} has an invalid status`);
  }

  for (const field of ["groupOrder", "parentItemOrder"]) {
    if (data[field] !== undefined && !isFiniteNumber(data[field])) {
      fail("documents_metadata_invalid", `${sourcePath} has invalid ${field}`);
    }
  }
  for (const field of ["hide"]) {
    if (data[field] !== undefined && typeof data[field] !== "boolean") {
      fail("documents_metadata_invalid", `${sourcePath} has invalid ${field}`);
    }
  }
  for (const field of ["purpose", "author", "version", "reading", "source"]) {
    if (data[field] !== undefined && !isNonEmptyString(data[field])) {
      fail("documents_metadata_invalid", `${sourcePath} has invalid ${field}`);
    }
  }
  if (data.editedAt !== undefined) {
    validateIsoDate(data.editedAt, sourcePath);
  }

  return Object.fromEntries(
    Object.entries(data).filter(([, value]) => value !== undefined),
  );
}

function resolveGroupOrders(documents) {
  const groupOrders = new Map();
  const parentOrders = new Map();

  for (const document of documents) {
    const groupOrder = document.meta.groupOrder;
    const parentKey = `${document.meta.group}\u0000${document.meta.parentItem}`;
    const parentOrder = document.meta.parentItemOrder;
    if (groupOrder !== undefined) {
      groupOrders.set(
        document.meta.group,
        Math.max(groupOrders.get(document.meta.group) ?? -Infinity, groupOrder),
      );
    }
    if (parentOrder !== undefined) {
      parentOrders.set(
        parentKey,
        Math.max(parentOrders.get(parentKey) ?? -Infinity, parentOrder),
      );
    }
  }

  return { groupOrders, parentOrders };
}

function sortDocuments(documents) {
  const { groupOrders, parentOrders } = resolveGroupOrders(documents);

  return [...documents].sort((left, right) => {
    const groupOrder =
      (groupOrders.get(right.meta.group) ?? 0) -
      (groupOrders.get(left.meta.group) ?? 0);
    if (groupOrder !== 0) return groupOrder;

    const group = compareText(left.meta.group, right.meta.group);
    if (group !== 0) return group;

    const leftParentKey = `${left.meta.group}\u0000${left.meta.parentItem}`;
    const rightParentKey = `${right.meta.group}\u0000${right.meta.parentItem}`;
    const parentOrder =
      (parentOrders.get(rightParentKey) ?? 0) -
      (parentOrders.get(leftParentKey) ?? 0);
    if (parentOrder !== 0) return parentOrder;

    const parent = compareText(left.meta.parentItem, right.meta.parentItem);
    if (parent !== 0) return parent;

    const documentOrder = right.meta.order - left.meta.order;
    if (documentOrder !== 0) return documentOrder;

    const title = compareText(left.meta.title, right.meta.title);
    if (title !== 0) return title;

    const canonicalUrl = compareText(left.canonicalUrl, right.canonicalUrl);
    if (canonicalUrl !== 0) return canonicalUrl;

    return (
      DOCUMENT_LOCALES.indexOf(left.contentLocale) -
      DOCUMENT_LOCALES.indexOf(right.contentLocale)
    );
  });
}

function createRegistrySource(documents) {
  const serializedDocuments = JSON.stringify(documents, null, 2);
  const imports = documents
    .map(
      (document) =>
        `  ${JSON.stringify(document.sourcePath)}: () => import(${JSON.stringify(`../content/${document.sourcePath}`)}),`,
    )
    .join("\n");

  return [
    "// This file is generated by scripts/generate-documents-content.mjs. Do not edit.",
    "",
    `export const documents = ${serializedDocuments} as const;`,
    "",
    "export type GeneratedDocument = (typeof documents)[number];",
    "",
    "export const documentModules = {",
    imports,
    "} as const;",
    "",
  ].join("\n");
}

const normalizeSearchText = (value) =>
  value
    .toLowerCase()
    .replace(/ё/gu, "е")
    .replace(/[^\p{Letter}\p{Number}\s]/gu, " ")
    .replace(/\s+/gu, " ")
    .trim();

function createSearchIndexJson(documents) {
  const locales = Object.fromEntries(
    DOCUMENT_LOCALES.map((locale) => [
      locale,
      (() => {
        const localizedDocuments = documents.filter(
          (document) =>
            document.contentLocale === locale && isProductionVisible(document),
        );

        return {
          pages: localizedDocuments.map((document, order) => ({
            type: "page",
            title: document.meta.title,
            description: document.meta.description,
            href: document.href,
            group: document.meta.group,
            parentItem: document.meta.parentItem,
            order,
            searchText: normalizeSearchText(
              [
                document.meta.title,
                document.meta.description,
                document.meta.group,
                document.meta.parentItem,
                document.canonicalUrl,
              ].join(" "),
            ),
            titleText: normalizeSearchText(document.meta.title),
          })),
          headings: localizedDocuments.flatMap((document, documentOrder) =>
            document.headings.map((heading, headingOrder) => ({
              type: "heading",
              title: heading.title,
              href: `${document.href}#${heading.id}`,
              pageTitle: document.meta.title,
              group: document.meta.group,
              parentItem: document.meta.parentItem,
              order: documentOrder * 10000 + headingOrder,
              searchText: normalizeSearchText(
                [
                  heading.title,
                  document.meta.title,
                  document.meta.description,
                  document.meta.group,
                  document.meta.parentItem,
                ].join(" "),
              ),
              titleText: normalizeSearchText(heading.title),
            })),
          ),
        };
      })(),
    ]),
  );

  return `${JSON.stringify({ schemaVersion: 1, locales })}\n`;
}

export async function compileDocumentsContent({ contentRoot, publicRoot }) {
  const files = await findContentFiles(contentRoot);
  const documents = [];
  const variants = new Map();
  const sourceByPath = new Map();

  for (const path of files) {
    const parsedPath = parseContentPath(contentRoot, path);
    const key = `${parsedPath.canonicalUrl}\u0000${parsedPath.contentLocale}`;
    if (variants.has(key)) {
      fail(
        "documents_duplicate_locale",
        `${parsedPath.sourcePath} and ${variants.get(key).sourcePath} both define ${parsedPath.contentLocale} for ${parsedPath.canonicalUrl}`,
      );
    }

    const parsedFile = matter(await readFile(path, "utf8"));
    const meta = validateMetadata(parsedFile.data, parsedPath.sourcePath);
    validateMdxSyntax(parsedFile.content, parsedPath.sourcePath);
    sourceByPath.set(parsedPath.sourcePath, parsedFile.content);
    const document = {
      ...parsedPath,
      slug:
        parsedPath.canonicalUrl === "index"
          ? []
          : parsedPath.canonicalUrl.split("/"),
      href:
        parsedPath.canonicalUrl === "index"
          ? "/docs"
          : `/docs/${parsedPath.canonicalUrl}`,
      headings: extractHeadings(parsedFile.content),
      meta,
    };
    variants.set(key, document);
    documents.push(document);
  }

  validatePublishedLocales(documents);
  await validateContentTargets(documents, sourceByPath, publicRoot);

  const availableLocalesByUrl = new Map();
  for (const document of documents) {
    const locales =
      availableLocalesByUrl.get(document.canonicalUrl) ?? new Set();
    locales.add(document.contentLocale);
    availableLocalesByUrl.set(document.canonicalUrl, locales);
  }

  const orderedDocuments = sortDocuments(
    documents.map((document) => ({
      ...document,
      availableLocales: DOCUMENT_LOCALES.filter((locale) =>
        availableLocalesByUrl.get(document.canonicalUrl).has(locale),
      ),
    })),
  );

  return {
    registrySource: createRegistrySource(orderedDocuments),
    searchIndexJson: createSearchIndexJson(orderedDocuments),
    documents: orderedDocuments,
    diagnostics: [],
  };
}

async function expectedArtifacts(options) {
  const result = await compileDocumentsContent(options);
  return [
    [options.registryPath, result.registrySource],
    [options.searchIndexPath, result.searchIndexJson],
  ];
}

export async function writeDocumentsArtifacts(options) {
  for (const [path, content] of await expectedArtifacts(options)) {
    if (!path) {
      fail(
        "documents_artifact_path_missing",
        "registryPath and searchIndexPath are required",
      );
    }
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, content, "utf8");
  }
}

export async function checkDocumentsArtifacts(options) {
  for (const [path, expected] of await expectedArtifacts(options)) {
    if (!path) {
      fail(
        "documents_artifact_path_missing",
        "registryPath and searchIndexPath are required",
      );
    }

    let actual;
    try {
      actual = await readFile(path, "utf8");
    } catch (error) {
      if (error && typeof error === "object" && error.code === "ENOENT") {
        fail("documents_artifact_missing", `${path} is missing`);
      }
      throw error;
    }

    if (actual !== expected) {
      fail(
        "documents_artifact_stale",
        `${path} does not match the deterministic compiler output`,
      );
    }
  }
}
