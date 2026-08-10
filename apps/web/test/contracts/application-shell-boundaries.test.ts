/** @jest-environment node */

import { execFileSync, spawnSync } from "node:child_process";
import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, relative, resolve } from "node:path";

import {
  findMobileTableContainmentIssues,
  findSensitiveShellDisclosures,
} from "../../e2e/support/application-shell-evidence";

const webRoot = process.cwd();
const sourceRoot = resolve(webRoot, "src");
const generatedRoot = resolve(sourceRoot, "lib/api/generated");
const sourceExtensionPattern = /\.(?:js|jsx|mjs|cjs|ts|tsx|mts|cts)$/;

function sourceFiles(directory: string): string[] {
  return execFileSync("find", [directory, "-type", "f"], { encoding: "utf8" })
    .trim()
    .split("\n")
    .filter(
      (path) =>
        path.length > 0 &&
        sourceExtensionPattern.test(path) &&
        !path.startsWith(`${generatedRoot}/`),
    );
}

it("keeps the completed application shell free of server-only full-stack source", () => {
  for (const path of sourceFiles(sourceRoot)) {
    const source = readFileSync(path, "utf8");
    const localPath = relative(webRoot, path);

    expect({ localPath, source }).toEqual({
      localPath,
      source: expect.not.stringMatching(/["']use server["']/),
    });
    expect({ localPath, source }).toEqual({
      localPath,
      source: expect.not.stringMatching(/@prisma|better-auth/iu),
    });
  }
});

it("keeps the source tree clean under the structured transport and storage scanner", () => {
  const result = spawnSync(process.execPath, ["scripts/check-boundaries.mjs"], {
    cwd: webRoot,
    encoding: "utf8",
  });
  expect(result.status).toBe(0);
  expect(result.stdout).toContain(
    "Web dependency and source boundaries are clean.",
  );
  expect(result.stderr).toBe("");
});

function checkFixture(source: string) {
  const fixture = resolve(
    sourceRoot,
    "__application_shell_boundary_test__/boundary-fixture.ts",
  );
  mkdirSync(dirname(fixture), { recursive: true });
  writeFileSync(fixture, source);

  try {
    return spawnSync(process.execPath, ["scripts/check-boundaries.mjs"], {
      cwd: webRoot,
      encoding: "utf8",
    });
  } finally {
    rmSync(dirname(fixture), { force: true, recursive: true });
  }
}

function expectBoundaryViolation(
  label: string,
  source: string,
  message: string,
) {
  const result = checkFixture(source);
  expect({ label, status: result.status }).toEqual({ label, status: 1 });
  expect(result.stderr).toContain(
    `${message}: src/__application_shell_boundary_test__/boundary-fixture.ts`,
  );
}

it("enforces the conservative reserved-capability boundary syntactically", () => {
  const rejected = [
    [
      "original fully pre-bound storage finding",
      'const save = localStorage.setItem.bind(localStorage, "token", value);\nsave();\n',
      "browser credential storage",
    ],
    [
      "original computed fetch finding",
      'const method = "fetch";\nwindow[method]("/api/v1/account");\n',
      "raw fetch outside generated runtime",
    ],
    [
      "direct fetch alias",
      "const request = fetch;\nvoid request;\n",
      "raw fetch outside generated runtime",
    ],
    [
      "call and apply composition",
      'const invoke = globalThis["fetch"].call.bind(globalThis.fetch);\nvoid invoke;\n',
      "raw fetch outside generated runtime",
    ],
    [
      "higher-order identity",
      "const identity = <T>(value: T) => value;\nidentity(sessionStorage);\n",
      "browser credential storage",
    ],
    [
      "spread capability",
      "const copy = { ...localStorage };\nvoid copy;\n",
      "browser credential storage",
    ],
    [
      "branch capability",
      "const request = enabled ? fetch : fallback;\nvoid request;\n",
      "raw fetch outside generated runtime",
    ],
    [
      "recursive capability",
      'function requestAgain() {\n  return fetch("/health").then(requestAgain);\n}\n',
      "raw fetch outside generated runtime",
    ],
    [
      "destructured capability",
      "const { localStorage: preferences } = window;\nvoid preferences;\n",
      "browser credential storage",
    ],
    [
      "computed const property",
      'const capability = "sessionStorage";\nwindow[capability].clear();\n',
      "browser credential storage",
    ],
    [
      "shadowed reserved identifier",
      'const localStorage = new Map<string, string>();\nlocalStorage.set("theme", "dark");\n',
      "browser credential storage",
    ],
    [
      "direct safe preference is still reserved",
      'localStorage.setItem("template.theme", "dark");\n',
      "browser credential storage",
    ],
    [
      "statically spelled object property",
      "const capabilities = { sessionStorage: memoryStore };\nvoid capabilities;\n",
      "browser credential storage",
    ],
    [
      "statically spelled string capability",
      'const capabilityName = "fetch";\nvoid capabilityName;\n',
      "raw fetch outside generated runtime",
    ],
  ] as const;

  for (const [label, source, message] of rejected) {
    expectBoundaryViolation(label, source, message);
  }
});

it("rejects literal and template raw product API paths without evaluating synthesized strings", () => {
  const rejected = [
    ["relative literal", 'const path = "/api/v1/account";\n'],
    [
      "absolute literal",
      'const path = "https://example.test/api/v1/organizations";\n',
    ],
    ["no-substitution template", "const path = `/api/v1/account`;\n"],
    [
      "template head",
      "const path = `/api/v1/organizations/${organizationId}/teams`;\n",
    ],
    ["wildcard route pattern", 'const path = "**/api/v1/auth/session";\n'],
  ] as const;

  for (const [label, source] of rejected) {
    expectBoundaryViolation(label, source, "raw product API path");
  }
});

it("allows approved wrappers and unrelated browser or string syntax", () => {
  const allowed = [
    [
      "generated SDK and approved preference wrappers",
      'import { getAccount } from "@/src/lib/api/generated";\nimport { serializeSidebarPreference } from "@/src/features/application/ui/sidebar-state";\nimport { useTheme } from "next-themes";\nvoid getAccount;\nvoid useTheme;\ndocument.cookie = serializeSidebarPreference(true);\n',
    ],
    ["unrelated browser location", 'window.location.assign("/docs");\n'],
    [
      "non-product strings",
      'const labels = ["fetching", "/api/v2/account", "/api/v10/account"];\nvoid labels;\n',
    ],
    [
      "dynamic non-reserved property",
      "declare const propertyName: string;\nconst capability = window[propertyName];\nvoid capability;\n",
    ],
    [
      "dynamic string synthesis is not evaluated",
      'const propertyName = ["fe", "tch"].join("");\nconst capability = window[propertyName];\nvoid capability;\n',
    ],
  ] as const;

  for (const [label, source] of allowed) {
    const result = checkFixture(source);
    expect({ label, stderr: result.stderr, status: result.status }).toEqual({
      label,
      stderr: "",
      status: 0,
    });
  }
});

it("excludes generated SDK source at the integration boundary", () => {
  const fixture = resolve(generatedRoot, "__boundary-fixture.gen.ts");
  writeFileSync(
    fixture,
    '// This file is auto-generated by @hey-api/openapi-ts\nexport const raw = () => fetch("/api/v1/account");\n',
  );

  try {
    const result = spawnSync(
      process.execPath,
      ["scripts/check-boundaries.mjs"],
      {
        cwd: webRoot,
        encoding: "utf8",
      },
    );
    expect(result.status).toBe(0);
    expect(result.stdout).toContain(
      "Web dependency and source boundaries are clean.",
    );
    expect(result.stderr).toBe("");
  } finally {
    rmSync(fixture, { force: true });
  }
});

it("pins the vertical-axis modifier to the dashboard DndContext", () => {
  const source = readFileSync(
    resolve(webRoot, "src/components/dashboard/activity-table.tsx"),
    "utf8",
  );

  expect(source).toContain(
    'import { restrictToVerticalAxis } from "@dnd-kit/modifiers";',
  );
  expect(source).toMatch(
    /<DndContext[\s\S]*?modifiers=\{\[restrictToVerticalAxis\]\}[\s\S]*?>/u,
  );
});

it("keeps the Route Handler exception closed", () => {
  const checker = readFileSync(
    resolve(webRoot, "scripts/check-boundaries.mjs"),
    "utf8",
  );
  expect(checker).toMatch(
    /const allowedRouteHandlers = new Set\(\[\s*"src\/app\/\(documents\)\/docs\/og\/\[\.\.\.slug\]\/route\.ts",?\s*\]\);/,
  );
});

it("requires both streamed shell readiness and client hydration", () => {
  const readiness = readFileSync(
    resolve(webRoot, "e2e/support/app-readiness.ts"),
    "utf8",
  );
  expect(readiness).toContain("APP_HYDRATED_ATTRIBUTE");
  expect(readiness).toMatch(
    /locator\("html"\)[\s\S]*toHaveAttribute\([\s\S]*APP_HYDRATED_ATTRIBUTE,[\s\S]*"true"/,
  );
});

it("waits for route-specific readiness with a scoped navigation budget", () => {
  const readiness = readFileSync(
    resolve(webRoot, "e2e/support/app-readiness.ts"),
    "utf8",
  );
  expect(readiness).toContain("APPLICATION_NAVIGATION_TIMEOUT_MS = 15_000");
  expect(readiness).toMatch(
    /waitForNavigationReady\([\s\S]*toHaveURL\([\s\S]*APPLICATION_NAVIGATION_TIMEOUT_MS[\s\S]*readyLocator[\s\S]*toBeVisible\([\s\S]*APPLICATION_NAVIGATION_TIMEOUT_MS/,
  );

  for (const journey of [
    "e2e/application-shell.spec.ts",
    "e2e/authentication.spec.ts",
  ]) {
    const source = readFileSync(resolve(webRoot, journey), "utf8");
    expect({ journey, source }).toEqual({
      journey,
      source: expect.stringContaining("waitForNavigationReady"),
    });
    expect({ journey, source }).toEqual({
      journey,
      source: expect.not.stringMatching(
        /expect\((?:page|secondPage)\)\.toHaveURL/,
      ),
    });
  }

  const authenticationJourney = readFileSync(
    resolve(webRoot, "e2e/authentication.spec.ts"),
    "utf8",
  );
  expect(authenticationJourney).toContain("test.setTimeout(90_000)");
});

it("keeps teardown authentication independent from logout and checks every likely shell surface", () => {
  const shellJourney = readFileSync(
    resolve(webRoot, "e2e/application-shell.spec.ts"),
    "utf8",
  );
  expect(shellJourney).toContain(
    'createContext(\n    "desktop application shell cleanup",\n  )',
  );
  expect(shellJourney).toMatch(
    /createLocalUser\(\s*cleanupContext,[\s\S]*?createOrganization\(\s*owner,\s*cleanupContext\.request,/,
  );
  const logoutAssertion = shellJourney.indexOf(
    'await waitForNavigationReady(\n    page,\n    "/auth/login",',
  );
  expect(logoutAssertion).toBeGreaterThan(-1);
  expect(shellJourney.slice(logoutAssertion)).not.toContain(
    "signInLocalAutomationUser",
  );

  // Definition plus desktop dashboard/workspaces/settings/profile and mobile
  // dashboard/workspaces/settings calls.
  expect(shellJourney.match(/expectNoSensitiveShellText\(/g)).toHaveLength(8);
  const disclosureHelper = shellJourney.slice(
    shellJourney.indexOf("async function expectNoSensitiveShellText"),
    shellJourney.indexOf("async function expectDashboard"),
  );
  expect(disclosureHelper).toContain("findSensitiveShellDisclosures");
  expect(disclosureHelper).not.toContain("not.toMatch");
});

it("classifies sensitive shell disclosures without rejecting product copy", () => {
  const secret = "E2E-Application-Shell-123!";
  const unsafe = [
    [secret, "configured secret"],
    ["password: super-secret", "password value"],
    ["nextCursor = eyJwYWdlIjoyfQ", "opaque cursor"],
    ['{"nextCursor":"browser-page-three"}', "opaque cursor"],
    ['{"previous_cursor":"browser-page-two"}', "opaque cursor"],
    ["Cursor: opaque-pagination-value", "opaque cursor"],
    ["ProblemDetails traceId: 00-abcd", "raw API error"],
    ["GET /api/v1/organizations returned HTTP 500", "raw API error"],
    ["Raw API response", "raw API error"],
    ["GET /api/v1 returned HTTP 503", "raw API error"],
    ["Authorization: Bearer abc.def.ghi", "authentication material"],
    ["XSRF-TOKEN=opaque", "authentication material"],
    ['{"x-csrf-token":"opaque"}', "authentication material"],
    ['{"cookie":"XSRF-TOKEN=opaque"}', "authentication material"],
    ['{"x-xsrf-token":"opaque"}', "authentication material"],
    ["Cookie: XSRF-TOKEN=opaque", "authentication material"],
    ["Cookie: __Secure-session=opaque", "authentication material"],
    ["Set-Cookie: __Host-session=opaque", "authentication material"],
    ["X-CSRF-TOKEN: opaque", "authentication material"],
    ["Dashboard edits are saved to the server.", "persistence claim"],
    ["Dashboard state is persisted.", "persistence claim"],
    ["Dashboard settings are saved.", "persistence claim"],
    ["Dashboard preferences persist in the database.", "persistence claim"],
    ["The server saves dashboard state.", "persistence claim"],
    ["The database persists dashboard settings.", "persistence claim"],
    ["Changes are persisted to the server.", "persistence claim"],
  ] as const;

  for (const [text, expected] of unsafe) {
    expect(findSensitiveShellDisclosures(text, [secret])).toContain(expected);
  }

  expect(
    findSensitiveShellDisclosures(
      "Demo changes are not saved. API keys authenticate integrations. Session management is available in Security.",
      [secret],
    ),
  ).toEqual([]);
  expect(
    findSensitiveShellDisclosures(
      "Demo changes were not saved. Demo edits aren't persisted. Dashboard changes never persist.",
      [secret],
    ),
  ).toEqual([]);
  expect(
    findSensitiveShellDisclosures(
      "Demo changes won't persist. Demo settings won't save. Dashboard state will not persist. The server does not save demo changes.",
      [secret],
    ),
  ).toEqual([]);
});

it("requires real mobile overflow inside viewport-contained table bounds", () => {
  const contained = {
    clientWidth: 358,
    containerLeft: 16,
    containerRight: 374,
    overflowX: "auto",
    scrollWidth: 704,
    tableLeft: 16,
    tableRight: 720,
    tableWidth: 704,
    viewportWidth: 390,
  } as const;
  expect(findMobileTableContainmentIssues(contained)).toEqual([]);

  expect(
    findMobileTableContainmentIssues({
      ...contained,
      containerLeft: -1,
      containerRight: 391,
      scrollWidth: contained.clientWidth,
      tableRight: 374,
      tableWidth: 358,
    }),
  ).toEqual(
    expect.arrayContaining([
      "container starts outside viewport",
      "container ends outside viewport",
      "table does not overflow",
      "table is not wider than its container",
    ]),
  );
});
