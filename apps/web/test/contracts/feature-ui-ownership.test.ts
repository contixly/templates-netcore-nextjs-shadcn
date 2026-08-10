import { existsSync, readdirSync } from "node:fs";
import { resolve } from "node:path";

const legacyDirectories = [
  "src/components/account",
  "src/components/api-keys",
  "src/components/application",
  "src/components/authentication",
  "src/components/collaboration",
  "src/components/dashboard",
  "src/components/documents",
  "src/components/organizations",
  "src/components/system",
];

test("domain presentation does not remain under shared components", () => {
  for (const directory of legacyDirectories) {
    expect(existsSync(resolve(process.cwd(), directory))).toBe(false);
  }
});

test("shared components contain only UI primitives", () => {
  expect(readdirSync(resolve(process.cwd(), "src/components"))).toEqual(["ui"]);
});
