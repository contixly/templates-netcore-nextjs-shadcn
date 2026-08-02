/** @jest-environment node */

import { readFileSync } from "node:fs";
import { resolve } from "node:path";

it("keeps document search on generated transport boundaries", () => {
  const source = readFileSync(
    resolve(process.cwd(), "src/lib/api/documents/browser/search-documents.ts"),
    "utf8",
  );

  expect(source).toContain("@/src/lib/api/browser/client");
  expect(source).toContain("@/src/lib/api/generated/sdk.gen");
  expect(source).toContain("@/src/lib/api/generated/types.gen");
  expect(source).toContain("normalizeApiFailure");
  expect(source).not.toMatch(/\bfetch\s*\(/);
  expect(source).not.toMatch(/["']use server["']/);
  expect(source).not.toMatch(
    /(?:interface|type)\s+(?:DocumentSearchResponse|DocumentSearchPageResponse|DocumentSearchHeadingResponse)\b/,
  );
});
