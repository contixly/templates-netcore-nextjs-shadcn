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

it("rejects structural raw-fetch and sensitive browser-storage variants", () => {
  const rejected = [
    [
      "optional window fetch",
      'window.fetch?.("/api/v1/status");\n',
      "raw fetch outside generated runtime",
    ],
    [
      "fetch call",
      'fetch.call(globalThis, "/api/v1/status");\n',
      "raw fetch outside generated runtime",
    ],
    [
      "bound fetch alias",
      'const apiFetch = fetch.bind(globalThis);\napiFetch("/api/v1/status");\n',
      "raw fetch outside generated runtime",
    ],
    [
      "window fetch alias",
      'const apiFetch = window.fetch;\napiFetch("/api/v1/status");\n',
      "raw fetch outside generated runtime",
    ],
    [
      "destructured fetch alias",
      'const { fetch: apiFetch } = globalThis;\napiFetch("/api/v1/status");\n',
      "raw fetch outside generated runtime",
    ],
    [
      "multiline password value",
      'const passwordValue = "sensitive";\nlocalStorage.setItem(\n  "theme",\n  passwordValue,\n);\n',
      "browser credential storage",
    ],
    [
      "reversed secret key",
      'const secretStorageKey = "opaque";\nsessionStorage.setItem(\n  secretStorageKey,\n  "value",\n);\n',
      "browser credential storage",
    ],
    [
      "session cookie value",
      'sessionStorage.setItem("temporary", authCookie);\n',
      "browser credential storage",
    ],
    [
      "storage property assignment",
      'localStorage["sessionSecret"] = opaqueValue;\n',
      "browser credential storage",
    ],
    [
      "window storage",
      'window.localStorage.setItem("password", opaqueValue);\n',
      "browser credential storage",
    ],
    [
      "aliased window storage",
      'const store = window.sessionStorage;\nstore.setItem("secret", opaqueValue);\n',
      "browser credential storage",
    ],
    [
      "browser global alias",
      'const browser = window;\nbrowser.localStorage.setItem("password", value);\n',
      "browser credential storage",
    ],
    [
      "chained browser and storage aliases",
      'const root = globalThis;\nconst browser = root.window;\nconst store = browser.sessionStorage;\nstore.setItem("credential", value);\n',
      "browser credential storage",
    ],
    [
      "destructured storage alias",
      'const { localStorage: store } = window;\nstore.setItem("session", value);\n',
      "browser credential storage",
    ],
    [
      "destructured setItem alias",
      'const { setItem: save } = sessionStorage;\nsave("secret", value);\n',
      "browser credential storage",
    ],
    [
      "bound setItem alias",
      'const save = localStorage.setItem.bind(localStorage);\nsave("password", value);\n',
      "browser credential storage",
    ],
    [
      "direct setItem apply",
      'localStorage.setItem.apply(localStorage, ["token", value]);\n',
      "browser credential storage",
    ],
    [
      "aliased setItem apply",
      'const save = window.localStorage.setItem;\nsave.apply(window.localStorage, ["credential", value]);\n',
      "browser credential storage",
    ],
    [
      "destructured setItem apply",
      'const { setItem: save } = sessionStorage;\nsave.apply(sessionStorage, ["secret", value]);\n',
      "browser credential storage",
    ],
    [
      "bound setItem apply",
      'const save = localStorage.setItem.bind(localStorage);\nsave.apply(undefined, ["password", value]);\n',
      "browser credential storage",
    ],
    [
      "bound apply alias",
      'const applySave = localStorage.setItem.apply.bind(localStorage.setItem);\napplySave(localStorage, ["session", value]);\n',
      "browser credential storage",
    ],
    [
      "destructured apply alias",
      'const { apply: applySave } = sessionStorage.setItem;\napplySave(sessionStorage, ["bearer", value]);\n',
      "browser credential storage",
    ],
    [
      "constant sensitive key alias",
      'const key = "password";\nlocalStorage.setItem(key, value);\n',
      "browser credential storage",
    ],
  ] as const;

  for (const [label, source, message] of rejected) {
    const result = checkFixture(source);
    expect({ label, status: result.status }).toEqual({ label, status: 1 });
    expect(result.stderr).toContain(
      `${message}: src/__application_shell_boundary_test__/boundary-fixture.ts`,
    );
  }
});

it("allows known-safe preference storage despite unrelated sensitive words", () => {
  const allowed = [
    'const token = "render-only";\nlocalStorage.setItem("theme", "dark");\nvoid token;\n',
    'sessionStorage.setItem(\n  "sidebar-preference",\n  "collapsed",\n);\n',
    'localStorage.setItem("color-scheme", "system");\n',
    'const localStorage = new Map<string, string>();\nlocalStorage.set("session", "ui");\n',
    'const localStorage = new Map<string, string>();\nlocalStorage.setItem("session", "ui");\n',
    'localStorage.setItem.apply(localStorage, ["theme", "dark"]);\n',
    'const save = sessionStorage.setItem;\nconst args = ["sidebar-preference", "collapsed"];\nsave.apply(sessionStorage, args);\n',
    'const localStorage = new Map<string, string>();\nlocalStorage.setItem.apply(localStorage, ["session", "ui"]);\n',
    'const preferences = new Map<string, string>();\nconst { setItem } = preferences;\nsetItem?.apply(preferences, ["token", "visual-label"]);\n',
  ];

  for (const source of allowed) {
    const result = checkFixture(source);
    expect(result.status).toBe(0);
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
