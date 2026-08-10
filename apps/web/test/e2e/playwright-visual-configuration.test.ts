import { execFileSync } from "node:child_process";
import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";

const appRoot = process.cwd();
const snapshotDirectory = path.join(
  appRoot,
  "e2e/ui-reference-parity.spec.ts-snapshots",
);
const canonicalSnapshotPathTemplate =
  "{snapshotDir}/{testFileDir}/{testFileName}-snapshots/{arg}{-projectName}{ext}";

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

test("visual baselines use canonical platform-independent paths", () => {
  const configSource = readFileSync(
    path.join(appRoot, "playwright.config.ts"),
    "utf8",
  );
  expect(configSource).toContain("snapshotPathTemplate:");
  expect(configSource).toContain(`"${canonicalSnapshotPathTemplate}"`);

  const snapshotNames = readdirSync(snapshotDirectory).filter((name) =>
    name.endsWith(".png"),
  );
  expect(snapshotNames).toHaveLength(108);
  expect(
    snapshotNames.some((name) => /-(?:darwin|linux|win32)\.png$/u.test(name)),
  ).toBe(false);
  expect(snapshotNames).toContain("home-en-desktop-light.png");
  expect(snapshotNames).toContain("workspace-api-keys-ru-mobile-dark.png");
});

test("browser installation covers every configured browser engine", () => {
  const packageJson = JSON.parse(
    readFileSync(path.join(appRoot, "package.json"), "utf8"),
  ) as { scripts: Record<string, string> };
  expect(packageJson.scripts["e2e:install"]).toBe(
    "playwright install chromium webkit",
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

test("normal discovery retains the four-project visual matrix", () => {
  const visualTests = listedTests(false).filter((line) =>
    line.includes("ui-reference-parity"),
  );
  expect(visualTests).toHaveLength(4);
  expect(
    visualTests.map((line) => line.match(/^\[([^\]]+)\]/u)?.[1]).sort(),
  ).toEqual(["desktop-dark", "desktop-light", "mobile-dark", "mobile-light"]);
});
