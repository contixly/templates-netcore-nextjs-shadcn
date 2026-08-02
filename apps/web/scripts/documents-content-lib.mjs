import { mkdir, readFile, readdir, writeFile } from "node:fs/promises";
import { dirname, relative, resolve, sep } from "node:path";
import matter from "gray-matter";

export const DOCUMENT_LOCALES = ["en", "ru"];

const CONTENT_FILE_PATTERN = /\.(?:md|mdx)$/iu;
const LOCALE_SUFFIX_PATTERN = /\.(en|ru)(\.(?:md|mdx))$/iu;
const DOCUMENT_STATUSES = new Set(["draft", "review", "published", "archived"]);
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
  const contentLocale = localeMatch?.[1].toLowerCase() ?? "en";
  const canonicalSourcePath = localeMatch
    ? sourcePath.replace(LOCALE_SUFFIX_PATTERN, "$2")
    : sourcePath;
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
    hasExplicitLocale: Boolean(localeMatch),
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

function createSearchIndexJson(documents) {
  const locales = Object.fromEntries(
    DOCUMENT_LOCALES.map((locale) => [
      locale,
      {
        pages: documents
          .filter((document) => document.contentLocale === locale)
          .map((document, order) => ({
            type: "page",
            title: document.meta.title,
            description: document.meta.description,
            href: document.href,
            group: document.meta.group,
            parentItem: document.meta.parentItem,
            order,
            searchText: `${document.meta.title} ${document.meta.description}`,
            titleText: document.meta.title,
          })),
        headings: [],
      },
    ]),
  );

  return `${JSON.stringify({ schemaVersion: 1, locales })}\n`;
}

export async function compileDocumentsContent({ contentRoot, publicRoot }) {
  void publicRoot;
  const files = await findContentFiles(contentRoot);
  const documents = [];
  const variants = new Map();

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
      meta,
    };
    variants.set(key, document);
    documents.push(document);
  }

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
