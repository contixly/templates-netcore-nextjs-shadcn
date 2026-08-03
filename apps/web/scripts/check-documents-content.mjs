import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { checkDocumentsArtifacts } from "./documents-content-lib.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repositoryRoot = resolve(webRoot, "..", "..");

await checkDocumentsArtifacts({
  contentRoot: resolve(webRoot, "src/features/documents/content"),
  publicRoot: resolve(webRoot, "public"),
  registryPath: resolve(
    webRoot,
    "src/features/documents/generated/documents-registry.gen.ts",
  ),
  searchIndexPath: resolve(
    repositoryRoot,
    "contracts/documents/search-index.json",
  ),
});
