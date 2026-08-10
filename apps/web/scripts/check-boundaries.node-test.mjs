import assert from "node:assert/strict";
import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import { afterEach, test } from "node:test";
import { dirname, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const extensions = ["js", "jsx", "mjs", "cjs", "ts", "tsx", "mts", "cts"];
const fixtureRoots = [
  resolve(webRoot, "src/__boundary_guard_test__"),
  resolve(webRoot, "src/app/__boundary_guard_test__"),
  resolve(webRoot, "test/__boundary_guard_test__"),
  resolve(webRoot, "e2e/__boundary_guard_test__"),
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

async function expectAllowed(relativePath, content) {
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
      0,
      `${relative(webRoot, fixturePath)} was rejected:\n${result.stdout}${result.stderr}`,
    );
    assert.equal(result.stderr, "");
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

test("allows only the exact presentation-only documentation image handler", async () => {
  const guard = await readFile(
    resolve(webRoot, "scripts/check-boundaries.mjs"),
    "utf8",
  );

  assert.match(
    guard,
    /const allowedRouteHandlers = new Set\(\[\s*"src\/app\/\(documents\)\/docs\/og\/\[\.\.\.slug\]\/route\.ts",?\s*\]\);/,
  );
});

test("still rejects every Next Route Handler below the API namespace", async () => {
  for (const extension of extensions) {
    await expectViolation(
      `src/app/api/__boundary_guard_test__/route.${extension}`,
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

test("rejects handwritten document search transport DTOs", async () => {
  await expectViolation(
    "src/__boundary_guard_test__/documents-dto.ts",
    "export interface DocumentSearchResponse { pages: unknown[] }",
    /handwritten OpenAPI DTO/,
  );
});

test("rejects legacy domain presentation imports", async () => {
  await expectViolation(
    "src/__boundary_guard_test__/legacy-presentation-import.ts",
    'import "@/src/components/account/profile-form";\n',
    /legacy domain presentation import: src\/__boundary_guard_test__\/legacy-presentation-import\.ts/,
  );
});

test("rejects legacy domain presentation imports from tests", async () => {
  await expectViolation(
    "test/__boundary_guard_test__/legacy-presentation-import.ts",
    'import "@/src/components/authentication/login-form";\n',
    /legacy domain presentation import: test\/__boundary_guard_test__\/legacy-presentation-import\.ts/,
  );
});

test("rejects legacy domain presentation imports from end-to-end tests", async () => {
  await expectViolation(
    "e2e/__boundary_guard_test__/legacy-presentation-import.ts",
    'import "@/src/components/organizations/workspace-switcher";\n',
    /legacy domain presentation import: e2e\/__boundary_guard_test__\/legacy-presentation-import\.ts/,
  );
});

test("rejects legacy domain presentation imports from generated source", async () => {
  await expectViolation(
    "src/lib/api/generated/__legacy-presentation-boundary-fixture.gen.ts",
    '// This file is auto-generated by @hey-api/openapi-ts\nimport "@/src/components/system/status-card";\n',
    /legacy domain presentation import: src\/lib\/api\/generated\/__legacy-presentation-boundary-fixture\.gen\.ts/,
  );
});

test("allows shared UI primitive imports in source, test, and end-to-end code", async () => {
  for (const root of ["src", "test", "e2e"]) {
    await expectAllowed(
      `${root}/__boundary_guard_test__/shared-ui-import.ts`,
      'import "@/src/components/ui/button";\n',
    );
  }
});
