import assert from "node:assert/strict";
import { mkdtemp, mkdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, resolve } from "node:path";
import { afterEach, test } from "node:test";
import * as contentCompiler from "./documents-content-lib.mjs";
import "./documents-corpus.node-test.mjs";

const {
  checkDocumentsArtifacts,
  compileDocumentsContent,
  writeDocumentsArtifacts,
} = contentCompiler;

const fixtureRoots = [];

afterEach(async () => {
  await Promise.all(
    fixtureRoots
      .splice(0)
      .map((path) => rm(path, { force: true, recursive: true })),
  );
});

const metadata = {
  title: "Document",
  description: "A deterministic documentation fixture.",
  group: "Guides",
  groupOrder: 10,
  parentItem: "Overview",
  parentItemOrder: 10,
  order: 10,
  toc: true,
  purpose: "Test fixture",
  status: "published",
  author: "Test",
  version: "1.0.0",
  editedAt: "2026-08-02",
};

function frontmatter(values, body = "") {
  return `---\n${Object.entries(values)
    .map(([key, value]) => `${key}: ${JSON.stringify(value)}`)
    .join("\n")}\n---\n\n# ${values.title}\n\n${body}`;
}

async function runFixture(name) {
  const root = await mkdtemp(resolve(tmpdir(), `documents-content-${name}-`));
  fixtureRoots.push(root);
  const contentRoot = resolve(root, "content");
  const publicRoot = resolve(root, "public");
  await mkdir(publicRoot, { recursive: true });

  const files = {
    "index.en.mdx": frontmatter({
      ...metadata,
      title: "Home",
      group: "Home",
      groupOrder: 20,
    }),
    "index.ru.mdx": frontmatter({
      ...metadata,
      title: "Главная",
      group: "Главная",
      groupOrder: 20,
    }),
    "guides/start.en.md": frontmatter(
      { ...metadata, title: "Start" },
      "## Details\n",
    ),
    "guides/start.ru.md": frontmatter(
      { ...metadata, title: "Начало" },
      "## Details\n",
    ),
  };

  if (name === "unknown-field") {
    files["guides/start.en.md"] = frontmatter({
      ...metadata,
      unsupported: "no",
    });
  }
  if (name === "reading-minutes") {
    files["guides/start.en.md"] = frontmatter({
      ...metadata,
      readingMinutes: 5,
    });
  }
  if (name === "duplicate-locale") {
    files["guides/start.en.mdx"] = frontmatter({
      ...metadata,
      title: "Duplicate",
    });
  }
  if (name === "bad-date") {
    files["guides/start.en.md"] = frontmatter({
      ...metadata,
      editedAt: "2026-02-30",
    });
  }
  if (name === "broken-link") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "[Missing](/docs/missing)\n",
    );
  }
  if (name === "broken-fragment") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      '<DocumentLinkCard href="/docs/guides/start?from=home#missing" title="Start" />\n',
    );
  }
  if (name === "missing-image") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      '![Missing](/img/missing.png "Missing")\n',
    );
  }
  if (name === "published-missing-ru") {
    delete files["guides/start.ru.md"];
  }
  if (name === "unknown-component") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "<UnknownComponent />\n",
    );
  }
  if (name === "content-export") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "export const unsafe = true\n",
    );
  }
  if (name === "fenced-content") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      [
        "~~~mdx",
        "## Not a heading",
        "[Missing](/docs/missing#missing)",
        "![Missing](/img/missing.png)",
        "<UnknownComponent />",
        "export const example = true",
        "~~~~",
        "",
        "[Start][start]",
        "[start]: /docs/guides/start/index/?from=fixture#details",
      ].join("\n"),
    );
  }

  await Promise.all(
    Object.entries(files).map(async ([relativePath, source]) => {
      const path = resolve(contentRoot, relativePath);
      await mkdir(dirname(path), { recursive: true });
      await writeFile(path, source, "utf8");
    }),
  );

  return {
    result: await compileDocumentsContent({ contentRoot, publicRoot }),
    contentRoot,
    publicRoot,
    root,
  };
}

test("discovers localized variants in deterministic navigation order", async () => {
  const { result } = await runFixture("valid");

  assert.equal(result.documents.length, 4);
  assert.equal(result.documents[0].canonicalUrl, "index");
  assert.deepEqual(result.documents[0].availableLocales, ["en", "ru"]);
  assert.deepEqual(result.diagnostics, []);
  assert.equal(result.searchIndexJson.includes("\r"), false);
  assert.match(result.registrySource, /export const documents/);
});

test("rejects unknown frontmatter fields", async () => {
  await assert.rejects(
    async () => (await runFixture("unknown-field")).result,
    /documents_metadata_unknown_field/,
  );
});

test("rejects readingMinutes outside the frontmatter allow-list", async () => {
  await assert.rejects(
    async () => (await runFixture("reading-minutes")).result,
    /documents_metadata_unknown_field/,
  );
});

test("rejects duplicate localized variants", async () => {
  await assert.rejects(
    async () => (await runFixture("duplicate-locale")).result,
    /documents_duplicate_locale/,
  );
});

test("rejects invalid editedAt dates", async () => {
  await assert.rejects(
    async () => (await runFixture("bad-date")).result,
    /documents_metadata_invalid_edited_at/,
  );
});

test("extracts stable duplicate heading identifiers outside code fences", () => {
  assert.deepEqual(
    contentCompiler.extractHeadings?.(
      "## Same\n```md\n## Hidden\n```\n## Same",
    ),
    [
      { level: 2, title: "Same", id: "same" },
      { level: 2, title: "Same", id: "same-2" },
    ],
  );
});

test("rejects broken internal document links", async () => {
  await assert.rejects(
    async () => (await runFixture("broken-link")).result,
    /documents_broken_link/,
  );
});

test("rejects links to missing generated heading fragments", async () => {
  await assert.rejects(
    async () => (await runFixture("broken-fragment")).result,
    /documents_broken_fragment/,
  );
});

test("rejects missing repository-local images", async () => {
  await assert.rejects(
    async () => (await runFixture("missing-image")).result,
    /documents_missing_image/,
  );
});

test("requires both locales for production-visible documents", async () => {
  await assert.rejects(
    async () => (await runFixture("published-missing-ru")).result,
    /documents_missing_published_locale/,
  );
});

test("rejects MDX components outside the closed component set", async () => {
  await assert.rejects(
    async () => (await runFixture("unknown-component")).result,
    /documents_unknown_mdx_component/,
  );
});

test("rejects executable MDX imports and exports", async () => {
  await assert.rejects(
    async () => (await runFixture("content-export")).result,
    /documents_mdx_module_syntax/,
  );
});

test("ignores headings, links, images, and MDX syntax in either fence style", async () => {
  const { result } = await runFixture("fenced-content");

  assert.deepEqual(result.diagnostics, []);
  assert.equal(
    result.documents.find((document) => document.sourcePath === "index.en.mdx")
      .headings.length,
    0,
  );
});

test("writes deterministic artifacts and detects missing or stale output", async () => {
  const fixture = await runFixture("artifacts");
  const registryPath = resolve(
    fixture.root,
    "generated",
    "documents-registry.gen.ts",
  );
  const searchIndexPath = resolve(
    fixture.root,
    "contracts",
    "search-index.json",
  );
  const options = { ...fixture, registryPath, searchIndexPath };

  await assert.rejects(
    checkDocumentsArtifacts(options),
    /documents_artifact_missing/,
  );
  await writeDocumentsArtifacts(options);
  await checkDocumentsArtifacts(options);
  await writeFile(searchIndexPath, "stale\n", "utf8");
  await assert.rejects(
    checkDocumentsArtifacts(options),
    /documents_artifact_stale/,
  );
});
