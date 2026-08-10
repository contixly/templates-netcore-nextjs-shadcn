import { execFileSync } from "node:child_process";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import path from "node:path";

const appRoot = process.cwd();
const snapshotDirectory = path.join(
  appRoot,
  "e2e/ui-reference-parity.spec.ts-snapshots",
);
const canonicalSnapshotPathTemplate =
  "{snapshotDir}/{testFileDir}/{testFileName}-snapshots/{arg}{-projectName}{ext}";
const visualEnvironmentHelper = path.join(
  appRoot,
  "scripts/visual-baseline-environment.mjs",
);
const runtimeSelectionHelper = path.join(
  appRoot,
  "scripts/playwright-runtime-selection.mjs",
);
const webServerHelper = path.join(appRoot, "scripts/run-e2e-web-server.mjs");

function helperJson(
  file: string,
  arguments_: readonly string[],
): Record<string, unknown> {
  if (!existsSync(file)) {
    return { missing: path.basename(file) };
  }
  return JSON.parse(
    execFileSync(process.execPath, [file, ...arguments_], {
      cwd: appRoot,
      encoding: "utf8",
    }),
  ) as Record<string, unknown>;
}

function listedTests(liveProviderSmoke: boolean): string[] {
  const npx = process.platform === "win32" ? "npx.cmd" : "npx";
  const childEnvironment = { ...process.env };
  delete childEnvironment.JEST_WORKER_ID;
  const output = execFileSync(npx, ["playwright", "test", "--list"], {
    cwd: appRoot,
    encoding: "utf8",
    env: {
      ...childEnvironment,
      E2E_LIVE_PROVIDER_SMOKE: liveProviderSmoke ? "1" : "0",
    },
  });
  return output
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.startsWith("["));
}

test("visual baselines use canonical names only inside the pinned runtime", () => {
  const configSource = readFileSync(
    path.join(appRoot, "playwright.config.ts"),
    "utf8",
  );
  expect(configSource).toContain("snapshotPathTemplate:");
  expect(configSource).toContain(`"${canonicalSnapshotPathTemplate}"`);
  expect(configSource).toContain("isCanonicalVisualBaselineEnvironment");
  expect(configSource).toContain("canonicalVisualBaselineEnvironment");

  const snapshotNames = readdirSync(snapshotDirectory).filter((name) =>
    name.endsWith(".png"),
  );
  expect(snapshotNames).toHaveLength(152);
  expect(
    snapshotNames.some((name) => /-(?:darwin|linux|win32)\.png$/u.test(name)),
  ).toBe(false);
  expect(snapshotNames).toContain("home-en-desktop-light.png");
  expect(snapshotNames).toContain(
    "organization-dashboard-en-activity-table-desktop-dark.png",
  );
  expect(snapshotNames).toContain(
    "docs-article-en-problem-details-mobile-light.png",
  );
  expect(snapshotNames).toContain("workspace-api-keys-ru-mobile-dark.png");
  expect(snapshotNames).toContain(
    "workspace-api-keys-ru-api-key-table-mobile-dark.png",
  );
});

test("canonical visual environment pins OS, architecture, browsers, and fonts", () => {
  expect(helperJson(visualEnvironmentHelper, ["--describe"])).toEqual(
    expect.objectContaining({
      arch: "arm64",
      chromiumRevision: "1228",
      chromiumVersion: "149.0.7827.55",
      fontProfile: "macOS SF Pro via system-ui/-apple-system",
      kernelRelease: "25.5.0",
      operatingSystem: "macOS 26.5.2",
      platform: "darwin",
      playwrightVersion: "1.61.1",
      webkitRevision: "2311",
      webkitVersion: "26.5",
    }),
  );
  const canonical = helperJson(visualEnvironmentHelper, [
    "--evaluate",
    JSON.stringify({
      arch: "arm64",
      chromiumRevision: "1228",
      chromiumVersion: "149.0.7827.55",
      fontProfile: "macOS SF Pro via system-ui/-apple-system",
      kernelRelease: "25.5.0",
      operatingSystem: "macOS 26.5.2",
      platform: "darwin",
      playwrightVersion: "1.61.1",
      webkitRevision: "2311",
      webkitVersion: "26.5",
    }),
  ]);
  expect(canonical).toEqual({ canonical: true, mismatches: [] });

  const noncanonical = helperJson(visualEnvironmentHelper, [
    "--evaluate",
    JSON.stringify({
      arch: "x64",
      chromiumRevision: "1228",
      chromiumVersion: "149.0.7827.55",
      fontProfile: "noncanonical system font profile",
      kernelRelease: "6.8.0",
      operatingSystem: "linux",
      platform: "linux",
      playwrightVersion: "1.61.1",
      webkitRevision: "2311",
      webkitVersion: "26.5",
    }),
  ]);
  expect(noncanonical).toEqual({
    canonical: false,
    mismatches: expect.arrayContaining([
      "platform: expected darwin, received linux",
      "arch: expected arm64, received x64",
      "kernelRelease: expected 25.5.0, received 6.8.0",
      "operatingSystem: expected macOS 26.5.2, received linux",
      "fontProfile: expected macOS SF Pro via system-ui/-apple-system, received noncanonical system font profile",
    ]),
  });
});

test("portable web-server helper constructs replicated HTTP and HTTPS plans", () => {
  const root = path.join(path.parse(appRoot).root, "workspace", "apps", "web");
  const temporaryRoot = path.join(path.parse(appRoot).root, "tmp", "visual");
  const http = helperJson(webServerHelper, [
    "--print-plan",
    "--root",
    root,
    "--temp-root",
    temporaryRoot,
    "--port",
    "3128",
    "--locale",
    "ru",
  ]);
  expect(http).toEqual(
    expect.objectContaining({
      certificate: null,
      directories: ["public", "src"],
      files: [
        "next.config.ts",
        "postcss.config.mjs",
        "tsconfig.json",
        "mdx-components.tsx",
        "package.json",
      ],
      locale: "ru",
      port: 3128,
      root,
      target: path.join(temporaryRoot, "netcore-nextjs-shadcn-e2e-ru-3128"),
    }),
  );
  expect(http.next).toEqual(
    expect.objectContaining({
      args: expect.arrayContaining([
        "dev",
        "--webpack",
        "--hostname",
        "127.0.0.1",
        "--port",
        "3128",
      ]),
      command: process.execPath,
    }),
  );

  const https = helperJson(webServerHelper, [
    "--print-plan",
    "--root",
    root,
    "--temp-root",
    temporaryRoot,
    "--port",
    "3130",
    "--locale",
    "ru",
    "--https",
  ]);
  expect(https.certificate).toEqual(
    expect.objectContaining({
      args: expect.arrayContaining([
        "req",
        "-x509",
        "-newkey",
        "rsa:2048",
        "-nodes",
      ]),
      command: "openssl",
    }),
  );
  expect(https.next).toEqual(
    expect.objectContaining({
      args: expect.arrayContaining([
        "--experimental-https",
        "--experimental-https-key",
        "--experimental-https-cert",
      ]),
    }),
  );
});

test("Playwright delegates copied deployments to the portable helper", () => {
  const configSource = readFileSync(
    path.join(appRoot, "playwright.config.ts"),
    "utf8",
  );
  expect(configSource).toContain(
    "node ./scripts/run-e2e-web-server.mjs --port 3128 --locale ru",
  );
  expect(configSource).toContain(
    "node ./scripts/run-e2e-web-server.mjs --port 3129 --locale en --https",
  );
  expect(configSource).toContain(
    "node ./scripts/run-e2e-web-server.mjs --port 3130 --locale ru --https",
  );
  expect(configSource).not.toContain('root="$(pwd)"');
  expect(configSource).not.toContain("cp -R");
  expect(configSource).not.toContain("openssl req");
});

test("browser installation covers every configured browser engine", () => {
  const packageJson = JSON.parse(
    readFileSync(path.join(appRoot, "package.json"), "utf8"),
  ) as { scripts: Record<string, string> };
  expect(packageJson.scripts["e2e:install"]).toBe(
    "playwright install chromium webkit",
  );
  expect(packageJson.scripts["e2e:visual"]).toContain(
    "visual-baseline-environment.mjs --assert",
  );
});

test("live-provider discovery is isolated from the visual matrix", () => {
  const tests = listedTests(true);
  expect(tests).toHaveLength(5);
  expect(tests.every((line) => line.startsWith("[desktop-light]"))).toBe(true);
  expect(tests.some((line) => line.includes("ui-reference-parity"))).toBe(
    false,
  );
});

test("runtime selection explicitly separates canonical pixels from portable behavior", () => {
  const configSource = readFileSync(
    path.join(appRoot, "playwright.config.ts"),
    "utf8",
  );
  expect(configSource).toContain("selectPlaywrightRuntime");
  expect(configSource).toContain(
    'runtimeSelection.visualServerIds.includes("russian")',
  );
  expect(configSource).toContain(
    'runtimeSelection.visualServerIds.includes("mobile")',
  );
  expect(configSource).toContain(
    'runtimeSelection.visualServerIds.includes("mobile-russian")',
  );

  const canonical = helperJson(runtimeSelectionHelper, [
    "--evaluate",
    JSON.stringify({ canonical: true, live: false }),
  ]);
  expect(canonical).toEqual({
    behavioralProjectNames: ["desktop-light"],
    mode: "canonical",
    projects: [
      { colorScheme: "light", device: "desktop", name: "desktop-light" },
      {
        colorScheme: "dark",
        device: "desktop",
        name: "desktop-dark",
        testMatch: "ui-reference-parity.spec.ts",
      },
      {
        colorScheme: "light",
        device: "mobile",
        name: "mobile-light",
        testMatch: "ui-reference-parity.spec.ts",
      },
      {
        colorScheme: "dark",
        device: "mobile",
        name: "mobile-dark",
        testMatch: "ui-reference-parity.spec.ts",
      },
    ],
    visualParityProjectNames: [
      "desktop-light",
      "desktop-dark",
      "mobile-light",
      "mobile-dark",
    ],
    visualServerIds: ["russian", "mobile", "mobile-russian"],
  });

  const noncanonical = helperJson(runtimeSelectionHelper, [
    "--evaluate",
    JSON.stringify({ canonical: false, live: false }),
  ]);
  expect(noncanonical).toEqual({
    behavioralProjectNames: ["desktop-light"],
    mode: "portable",
    projects: [
      {
        colorScheme: "light",
        device: "desktop",
        name: "desktop-light",
        testIgnore: "ui-reference-parity.spec.ts",
      },
    ],
    visualParityProjectNames: [],
    visualServerIds: [],
  });
});
