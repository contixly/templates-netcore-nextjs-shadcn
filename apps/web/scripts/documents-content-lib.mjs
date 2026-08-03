import { mkdir, readFile, readdir, stat, writeFile } from "node:fs/promises";
import { dirname, relative, resolve, sep } from "node:path";
import { createProcessor } from "@mdx-js/mdx";
import matter from "gray-matter";
import remarkGfm from "remark-gfm";

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
const ALLOWED_MDX_INTRINSIC_ELEMENTS = new Set([
  "a",
  "blockquote",
  "br",
  "code",
  "del",
  "div",
  "em",
  "h1",
  "h2",
  "h3",
  "h4",
  "h5",
  "h6",
  "hr",
  "img",
  "kbd",
  "li",
  "ol",
  "p",
  "pre",
  "span",
  "strong",
  "sub",
  "sup",
  "table",
  "tbody",
  "td",
  "tfoot",
  "th",
  "thead",
  "tr",
  "ul",
]);
const CUSTOM_MDX_ATTRIBUTE_CONTRACTS = new Map([
  ["Callout", { optional: ["title", "variant"] }],
  ["Steps", {}],
  ["Step", { required: ["title"] }],
  ["Files", {}],
  ["Folder", { required: ["name"] }],
  ["File", { required: ["name"] }],
  ["Tabs", { optional: ["defaultValue"] }],
  ["Tab", { required: ["title", "value"] }],
  ["DocumentLinkGrid", {}],
  ["DocumentLinkGroup", { required: ["title"], optional: ["description"] }],
  ["DocumentLinkCard", { required: ["href", "title"] }],
]);
const INTRINSIC_MDX_ATTRIBUTE_CONTRACTS = new Map([
  ["a", { optional: ["href", "title"] }],
  ["blockquote", { optional: ["cite"] }],
  ["code", { optional: ["className"] }],
  ["del", { optional: ["cite", "dateTime"] }],
  ["img", { required: ["src"], optional: ["alt", "height", "title", "width"] }],
  ["ol", { optional: ["start", "type"] }],
  ["td", { optional: ["colSpan", "rowSpan"] }],
  ["th", { optional: ["colSpan", "rowSpan", "scope"] }],
]);
const CALLOUT_VARIANTS = new Set([
  "default",
  "info",
  "success",
  "warning",
  "danger",
]);
const SAFE_DOCUMENT_SEGMENT_PATTERN = /^[a-z0-9]+(?:[.-][a-z0-9]+)*$/u;
const RESERVED_DOCUMENT_HEADING_IDS = [
  "document-title",
  "main-content",
  "footnote-label",
];
const RESERVED_GFM_HEADING_ID_PREFIXES = [
  "user-content-fn-",
  "user-content-fnref-",
];
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

const parseMdx = (content) =>
  createProcessor({ remarkPlugins: [remarkGfm] }).parse(content);

function visitMdxTree(node, visitor, ancestors = []) {
  visitor(node, ancestors);
  for (const child of node.children ?? []) {
    visitMdxTree(child, visitor, [...ancestors, node]);
  }
}

const isNamedMdxElement = (node, name) =>
  Boolean(
    node &&
    (node.type === "mdxJsxFlowElement" || node.type === "mdxJsxTextElement") &&
    node.name === name,
  );

function headingText(node) {
  if (node.type === "text" || node.type === "inlineCode") {
    return node.value;
  }
  if (node.type === "image") {
    return "";
  }
  return (node.children ?? []).map(headingText).join("");
}

const slugifyHeading = (title) =>
  title
    .trim()
    .toLowerCase()
    .replace(/ё/gu, "е")
    .replace(/[^\p{Letter}\p{Number}\s-]/gu, "")
    .replace(/\s+/gu, "-")
    .replace(/-+/gu, "-")
    .replace(/^-|-$/gu, "");

function articleHeadingBaseId(title) {
  const baseId = slugifyHeading(title) || "section";
  return RESERVED_GFM_HEADING_ID_PREFIXES.some((prefix) =>
    baseId.startsWith(prefix),
  )
    ? `document-heading-${baseId}`
    : baseId;
}

function allocateUniqueHeadingId(baseId, seenIds) {
  let count = (seenIds.get(baseId) ?? 0) + 1;
  let id = count === 1 ? baseId : `${baseId}-${count}`;
  while (seenIds.has(id)) {
    count += 1;
    id = `${baseId}-${count}`;
  }

  seenIds.set(baseId, count);
  seenIds.set(id, 1);
  return id;
}

export function extractHeadings(content, tree = parseMdx(content)) {
  const headings = [];
  const seenIds = new Map(RESERVED_DOCUMENT_HEADING_IDS.map((id) => [id, 1]));

  visitMdxTree(tree, (node) => {
    if (node.type !== "heading" || (node.depth !== 2 && node.depth !== 3)) {
      return;
    }
    const title = headingText(node).replace(/\s+/gu, " ").trim();

    const baseId = articleHeadingBaseId(title);
    headings.push({
      level: node.depth,
      title,
      id: allocateUniqueHeadingId(baseId, seenIds),
    });
  });

  return headings;
}

function validateMdxAttributes(node, sourcePath, line) {
  const contract = ALLOWED_MDX_COMPONENTS.has(node.name)
    ? CUSTOM_MDX_ATTRIBUTE_CONTRACTS.get(node.name)
    : (INTRINSIC_MDX_ATTRIBUTE_CONTRACTS.get(node.name) ?? {});
  const required = new Set(contract.required ?? []);
  const optional = new Set(contract.optional ?? []);
  const attributes = new Map();

  for (const attribute of node.attributes) {
    if (attribute.type !== "mdxJsxAttribute") continue;
    const attributeLine = attribute.position?.start.line ?? line;
    if (!required.has(attribute.name) && !optional.has(attribute.name)) {
      fail(
        "documents_mdx_attribute_unknown",
        `${sourcePath}:${attributeLine} does not allow ${attribute.name} on ${node.name}`,
      );
    }
    if (attributes.has(attribute.name)) {
      fail(
        "documents_mdx_attribute_duplicate",
        `${sourcePath}:${attributeLine} contains duplicate ${node.name}.${attribute.name}`,
      );
    }
    if (typeof attribute.value !== "string") {
      fail(
        "documents_mdx_attribute_invalid",
        `${sourcePath}:${attributeLine} requires quoted string ${node.name}.${attribute.name}`,
      );
    }
    if (required.has(attribute.name) && attribute.value.trim().length === 0) {
      fail(
        "documents_mdx_attribute_invalid",
        `${sourcePath}:${attributeLine} requires non-empty ${node.name}.${attribute.name}`,
      );
    }
    attributes.set(attribute.name, attribute.value);
  }

  for (const attributeName of required) {
    if (!attributes.has(attributeName)) {
      fail(
        "documents_mdx_attribute_required",
        `${sourcePath}:${line} requires ${node.name}.${attributeName}`,
      );
    }
  }

  const calloutVariant =
    node.name === "Callout" ? attributes.get("variant") : undefined;
  if (calloutVariant !== undefined && !CALLOUT_VARIANTS.has(calloutVariant)) {
    fail(
      "documents_mdx_attribute_invalid",
      `${sourcePath}:${line} has invalid Callout.variant`,
    );
  }

  const documentCardHref =
    node.name === "DocumentLinkCard" ? attributes.get("href") : undefined;
  if (
    documentCardHref !== undefined &&
    !/^\/docs(?:[/?#]|$)/u.test(documentCardHref)
  ) {
    fail(
      "documents_mdx_attribute_invalid",
      `${sourcePath}:${line} requires canonical DocumentLinkCard.href`,
    );
  }

  const tabsDefaultValue =
    node.name === "Tabs" ? attributes.get("defaultValue") : undefined;
  if (tabsDefaultValue !== undefined && tabsDefaultValue.trim().length === 0) {
    fail(
      "documents_mdx_attribute_invalid",
      `${sourcePath}:${line} requires non-empty Tabs.defaultValue`,
    );
  }
}

function validateMdxSyntax(content, sourcePath) {
  const tree = parseMdx(content);

  visitMdxTree(tree, (node, ancestors) => {
    const line = node.position?.start.line ?? 1;
    if (
      node.type === "heading" &&
      (node.depth === 2 || node.depth === 3) &&
      ancestors.some((ancestor) => ancestor.type === "footnoteDefinition")
    ) {
      fail(
        "documents_footnote_heading_unsupported",
        `${sourcePath}:${line} contains h2/h3 inside a GFM footnote definition`,
      );
    }
    if (
      node.type === "heading" &&
      (node.depth === 2 || node.depth === 3) &&
      ancestors.some(
        (ancestor) =>
          isNamedMdxElement(ancestor, "Tab") ||
          isNamedMdxElement(ancestor, "Tabs"),
      )
    ) {
      fail(
        "documents_tab_heading_unsupported",
        `${sourcePath}:${line} contains h2/h3 inside Tabs/Tab`,
      );
    }
    if (node.type === "mdxjsEsm") {
      fail(
        "documents_mdx_module_syntax",
        `${sourcePath}:${line} contains forbidden import/export syntax`,
      );
    }
    if (
      node.type === "mdxFlowExpression" ||
      node.type === "mdxTextExpression"
    ) {
      fail(
        "documents_mdx_expression_syntax",
        `${sourcePath}:${line} contains forbidden executable expression syntax`,
      );
    }
    if (
      node.type === "mdxJsxFlowElement" ||
      node.type === "mdxJsxTextElement"
    ) {
      if (node.name === "Tab" && !isNamedMdxElement(ancestors.at(-1), "Tabs")) {
        fail(
          "documents_tabs_structure_invalid",
          `${sourcePath}:${line} requires Tab to be a direct child of Tabs`,
        );
      }
      if (
        node.name &&
        !ALLOWED_MDX_INTRINSIC_ELEMENTS.has(node.name) &&
        !ALLOWED_MDX_COMPONENTS.has(node.name)
      ) {
        fail(
          "documents_unknown_mdx_component",
          `${sourcePath}:${line} uses ${node.name}`,
        );
      }
      const reservedFootnoteAttribute = node.attributes.find(
        (attribute) =>
          attribute.type === "mdxJsxAttribute" &&
          attribute.name === "data-footnote-ref",
      );
      if (reservedFootnoteAttribute) {
        fail(
          "documents_reserved_footnote_attribute",
          `${sourcePath}:${reservedFootnoteAttribute.position?.start.line ?? line} contains reserved data-footnote-ref syntax`,
        );
      }
      const srcSetAttribute = node.attributes.find(
        (attribute) =>
          attribute.type === "mdxJsxAttribute" &&
          attribute.name.toLowerCase() === "srcset",
      );
      if (srcSetAttribute) {
        fail(
          "documents_mdx_srcset_unsupported",
          `${sourcePath}:${srcSetAttribute.position?.start.line ?? line} contains unsupported srcSet syntax`,
        );
      }
      const expressionAttribute = node.attributes.find(
        (attribute) =>
          attribute.type === "mdxJsxExpressionAttribute" ||
          (attribute.type === "mdxJsxAttribute" &&
            attribute.value?.type === "mdxJsxAttributeValueExpression"),
      );
      if (expressionAttribute) {
        fail(
          "documents_mdx_expression_attribute",
          `${sourcePath}:${expressionAttribute.position?.start.line ?? line} contains forbidden expression-valued JSX attribute syntax`,
        );
      }
      validateMdxAttributes(node, sourcePath, line);
    }
  });

  visitMdxTree(tree, (node) => {
    if (!isNamedMdxElement(node, "Tabs")) return;

    const tabChildren = node.children ?? [];
    const invalidChild = tabChildren.find(
      (child) => !isNamedMdxElement(child, "Tab"),
    );
    if (invalidChild) {
      fail(
        "documents_tabs_structure_invalid",
        `${sourcePath}:${invalidChild.position?.start.line ?? node.position?.start.line ?? 1} requires every direct Tabs child to be Tab`,
      );
    }

    if (tabChildren.length === 0) {
      fail(
        "documents_tabs_structure_invalid",
        `${sourcePath}:${node.position?.start.line ?? 1} requires Tabs to contain at least one direct Tab`,
      );
    }

    const values = new Set();
    for (const child of tabChildren) {
      const value = child.attributes.find(
        (attribute) =>
          attribute.type === "mdxJsxAttribute" && attribute.name === "value",
      )?.value;
      if (values.has(value)) {
        fail(
          "documents_tabs_structure_invalid",
          `${sourcePath}:${child.position?.start.line ?? node.position?.start.line ?? 1} requires unique direct Tab.value attributes`,
        );
      }
      values.add(value);
    }

    const defaultValue = node.attributes.find(
      (attribute) =>
        attribute.type === "mdxJsxAttribute" &&
        attribute.name === "defaultValue",
    )?.value;
    if (defaultValue !== undefined && !values.has(defaultValue)) {
      fail(
        "documents_tabs_structure_invalid",
        `${sourcePath}:${node.position?.start.line ?? 1} requires Tabs.defaultValue to match a direct Tab.value`,
      );
    }
  });

  return tree;
}

function extractContentTargets(tree) {
  const targets = [];
  const definitions = new Map();

  visitMdxTree(tree, (node) => {
    if (node.type === "definition" && !definitions.has(node.identifier)) {
      definitions.set(node.identifier, node.url);
    }
  });

  visitMdxTree(tree, (node) => {
    const line = node.position?.start.line ?? 1;
    if (node.type === "link" || node.type === "image") {
      targets.push({
        kind: node.type === "image" ? "image" : "link",
        href: node.url,
        line,
      });
      return;
    }
    if (node.type === "linkReference" || node.type === "imageReference") {
      const href = definitions.get(node.identifier);
      if (href !== undefined) {
        targets.push({
          kind: node.type === "imageReference" ? "image" : "link",
          href,
          line,
        });
      }
      return;
    }
    if (
      node.type !== "mdxJsxFlowElement" &&
      node.type !== "mdxJsxTextElement"
    ) {
      return;
    }
    for (const attribute of node.attributes) {
      if (
        attribute.type === "mdxJsxAttribute" &&
        typeof attribute.value === "string" &&
        (attribute.name === "href" || attribute.name === "src")
      ) {
        targets.push({
          kind: attribute.name === "src" ? "image" : "link",
          href: attribute.value,
          line: attribute.position?.start.line ?? line,
        });
      }
    }
  });

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
  const pathname = withoutHash.split("?", 1)[0].replace(/\/+$/u, "");

  if (pathname !== "/docs" && !pathname.startsWith("/docs/")) {
    return undefined;
  }

  const rawTarget =
    pathname === "/docs" || pathname === "/docs/index"
      ? "index"
      : pathname.slice("/docs/".length);

  return {
    targetUrl: rawTarget || "index",
    fragment:
      rawFragment === undefined
        ? undefined
        : safeDecode(rawFragment, decodeURIComponent),
  };
}

function validateSafeLinkTarget(href, sourcePath, line) {
  const trimmed = href.trim();
  let parsed;
  try {
    parsed = new URL(trimmed, "https://documents.invalid");
  } catch {
    fail("documents_unsafe_link_target", `${sourcePath}:${line} -> ${href}`);
  }

  if (
    href !== trimmed ||
    !["http:", "https:", "mailto:"].includes(parsed.protocol)
  ) {
    fail("documents_unsafe_link_target", `${sourcePath}:${line} -> ${href}`);
  }
}

function normalizeImageHref(href) {
  const trimmed = href.trim();
  if (/^https?:\/\//iu.test(trimmed)) return { external: true };
  if (!trimmed.startsWith("/img/")) return undefined;
  const pathname = trimmed.split("#", 1)[0].split("?", 1)[0];
  return { external: false, pathname: safeDecode(pathname, decodeURI) };
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
      sourceByPath.get(document.sourcePath).tree,
    )) {
      if (target.kind === "link") {
        validateSafeLinkTarget(target.href, document.sourcePath, target.line);
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
      if (!imageHref) {
        fail(
          "documents_invalid_image_source",
          `${document.sourcePath}:${target.line} -> ${target.href}`,
        );
      }
      if (imageHref.external) continue;
      const absolutePublicRoot = resolve(publicRoot);
      const absoluteImageRoot = resolve(absolutePublicRoot, "img");
      const imagePath = resolve(
        absolutePublicRoot,
        imageHref.pathname.slice(1),
      );
      const imageRelativePath = relative(absoluteImageRoot, imagePath);
      let imageStat;
      if (
        imageRelativePath.startsWith(`..${sep}`) ||
        imageRelativePath === ".."
      ) {
        fail(
          "documents_invalid_image_source",
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

  if (
    withoutExtension !== "index" &&
    (canonicalUrl === "index" || canonicalUrl.endsWith("/index"))
  ) {
    fail(
      "documents_ambiguous_index_alias",
      `${sourcePath} still maps to terminal index canonical URL ${canonicalUrl}`,
    );
  }

  const invalidSegment = canonicalUrl
    .split("/")
    .find((segment) => !SAFE_DOCUMENT_SEGMENT_PATTERN.test(segment));
  if (invalidSegment !== undefined) {
    fail(
      "documents_invalid_slug",
      `${sourcePath} contains unsafe canonical route segment ${JSON.stringify(invalidSegment)}`,
    );
  }

  if (canonicalUrl === "og" || canonicalUrl.startsWith("og/")) {
    fail(
      "documents_reserved_slug",
      `${sourcePath} maps to reserved canonical URL ${canonicalUrl}`,
    );
  }

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
    .normalize("NFC")
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
    const tree = validateMdxSyntax(parsedFile.content, parsedPath.sourcePath);
    sourceByPath.set(parsedPath.sourcePath, { tree });
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
      headings: extractHeadings(parsedFile.content, tree),
      meta,
    };
    variants.set(key, document);
    documents.push(document);
  }

  await validateContentTargets(documents, sourceByPath, publicRoot);
  validatePublishedLocales(documents);

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
