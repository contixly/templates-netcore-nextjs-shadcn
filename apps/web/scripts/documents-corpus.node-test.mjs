import assert from "node:assert/strict";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import { compileDocumentsContent } from "./documents-content-lib.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

test("compiles the complete bilingual documentation corpus without diagnostics", async () => {
  const result = await compileDocumentsContent({
    contentRoot: resolve(webRoot, "src/features/documents/content"),
    publicRoot: resolve(webRoot, "public"),
  });

  assert.equal(result.documents.length, 108);
  assert.equal(
    new Set(result.documents.map((document) => document.canonicalUrl)).size,
    54,
  );
  assert.deepEqual(
    [
      ...new Set(result.documents.map((document) => document.contentLocale)),
    ].sort(),
    ["en", "ru"],
  );
  assert.deepEqual(result.diagnostics, []);
});
