import assert from "node:assert/strict";
import { mkdtemp, mkdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, resolve } from "node:path";
import { afterEach, test } from "node:test";
import {
  checkDocumentsArtifacts,
  compileDocumentsContent,
  writeDocumentsArtifacts,
} from "./documents-content-lib.mjs";

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

function frontmatter(values) {
  return `---\n${Object.entries(values)
    .map(([key, value]) => `${key}: ${JSON.stringify(value)}`)
    .join("\n")}\n---\n\n# ${values.title}\n`;
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
    "guides/start.en.md": frontmatter({ ...metadata, title: "Start" }),
    "guides/start.ru.md": frontmatter({ ...metadata, title: "Начало" }),
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
