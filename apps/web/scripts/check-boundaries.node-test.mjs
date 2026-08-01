import assert from "node:assert/strict";
import { mkdir, rm, writeFile } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import { afterEach, test } from "node:test";
import { dirname, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const extensions = ["js", "jsx", "mjs", "cjs", "ts", "tsx", "mts", "cts"];
const fixtureRoots = [
  resolve(webRoot, "src/__boundary_guard_test__"),
  resolve(webRoot, "src/app/__boundary_guard_test__"),
];

afterEach(async () => {
  await Promise.all(
    fixtureRoots.map((path) => rm(path, { force: true, recursive: true })),
  );
});

async function expectViolation(relativePath, content, expectedMessage) {
  const fixturePath = resolve(webRoot, relativePath);

  await mkdir(dirname(fixturePath), { recursive: true });
  await writeFile(fixturePath, content);

  try {
    const result = spawnSync(
      process.execPath,
      ["./scripts/check-boundaries.mjs"],
      {
        cwd: webRoot,
        encoding: "utf8",
      },
    );

    assert.equal(
      result.status,
      1,
      `${relative(webRoot, fixturePath)} was not rejected:\n${result.stdout}${result.stderr}`,
    );
    assert.match(result.stderr, expectedMessage);
  } finally {
    await rm(fixturePath);
  }
}

test("scans every enabled JavaScript and TypeScript source form", async () => {
  for (const extension of extensions) {
    await expectViolation(
      `src/__boundary_guard_test__/forbidden.${extension}`,
      'export const forbidden = () => fetch("/api/health");\n',
      /raw fetch outside generated runtime/,
    );
  }
});

test("rejects Route Handlers in every enabled source form", async () => {
  for (const extension of extensions) {
    await expectViolation(
      `src/app/__boundary_guard_test__/route.${extension}`,
      "export const value = 1;\n",
      /Next Route Handler/,
    );
  }
});

test("rejects handwritten authentication transport DTOs", async () => {
  await expectViolation(
    "src/__boundary_guard_test__/auth-dto.ts",
    "export type AuthSessionResponse = { authenticated: boolean };",
    /handwritten OpenAPI DTO/,
  );
});

test("rejects raw collaboration fetches", async () => {
  await expectViolation(
    "src/lib/api/collaboration/browser/raw-team-request.ts",
    'export const createTeam = () => fetch("/api/v1/organizations/id/teams");',
    /raw fetch outside generated runtime/,
  );
});

test("rejects handwritten collaboration transport DTOs", async () => {
  await expectViolation(
    "src/__boundary_guard_test__/collaboration-dto.ts",
    "export interface InvitationDecisionResponse { canRespond: boolean }",
    /handwritten OpenAPI DTO/,
  );
});
