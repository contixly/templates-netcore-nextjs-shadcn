import { createRequire } from "node:module";
import { arch, platform, release } from "node:os";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);

export const canonicalVisualBaselineEnvironment = Object.freeze({
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
});

function installedPlaywrightRuntime() {
  const packagePath = require.resolve("@playwright/test/package.json");
  const packageJson = JSON.parse(readFileSync(packagePath, "utf8"));
  const browsersPath = path.join(
    path.dirname(require.resolve("playwright-core/package.json")),
    "browsers.json",
  );
  const browsers = JSON.parse(readFileSync(browsersPath, "utf8")).browsers;
  const chromium = browsers.find((browser) => browser.name === "chromium");
  const webkit = browsers.find((browser) => browser.name === "webkit");
  if (!chromium || !webkit) {
    throw new Error(
      "The installed Playwright runtime is missing Chromium or WebKit.",
    );
  }
  return {
    chromiumRevision: chromium.revision,
    chromiumVersion: chromium.browserVersion,
    playwrightVersion: packageJson.version,
    webkitRevision: webkit.revision,
    webkitVersion: webkit.browserVersion,
  };
}

function installedOperatingSystem(currentPlatform) {
  if (currentPlatform !== "darwin") return currentPlatform;
  const version = execFileSync("sw_vers", ["-productVersion"], {
    encoding: "utf8",
  }).trim();
  return `macOS ${version}`;
}

function installedFontProfile(currentPlatform) {
  return currentPlatform === "darwin" &&
    existsSync("/System/Library/Fonts/SFNS.ttf")
    ? "macOS SF Pro via system-ui/-apple-system"
    : "noncanonical system font profile";
}

export function currentVisualBaselineEnvironment() {
  const currentPlatform = platform();
  return {
    arch: arch(),
    fontProfile: installedFontProfile(currentPlatform),
    kernelRelease: release(),
    operatingSystem: installedOperatingSystem(currentPlatform),
    platform: currentPlatform,
    ...installedPlaywrightRuntime(),
  };
}

export function visualBaselineEnvironmentMismatches(environment) {
  return [
    "platform",
    "arch",
    "kernelRelease",
    "operatingSystem",
    "fontProfile",
    "playwrightVersion",
    "chromiumRevision",
    "chromiumVersion",
    "webkitRevision",
    "webkitVersion",
  ].flatMap((key) =>
    environment[key] === canonicalVisualBaselineEnvironment[key]
      ? []
      : [
          `${key}: expected ${canonicalVisualBaselineEnvironment[key]}, received ${environment[key] ?? "missing"}`,
        ],
  );
}

export function isCanonicalVisualBaselineEnvironment(environment) {
  return visualBaselineEnvironmentMismatches(environment).length === 0;
}

function evaluate(environment) {
  const mismatches = visualBaselineEnvironmentMismatches(environment);
  return { canonical: mismatches.length === 0, mismatches };
}

function main(arguments_) {
  if (arguments_[0] === "--describe") {
    process.stdout.write(
      `${JSON.stringify(canonicalVisualBaselineEnvironment)}\n`,
    );
    return;
  }
  if (arguments_[0] === "--evaluate") {
    const value = arguments_[1];
    if (!value)
      throw new Error("--evaluate requires one JSON environment argument.");
    process.stdout.write(`${JSON.stringify(evaluate(JSON.parse(value)))}\n`);
    return;
  }
  if (arguments_[0] === "--assert") {
    const result = evaluate(currentVisualBaselineEnvironment());
    if (!result.canonical) {
      process.stderr.write(
        [
          "Visual pixel comparison is disabled outside the canonical baseline environment.",
          ...result.mismatches.map((mismatch) => `- ${mismatch}`),
        ].join("\n") + "\n",
      );
      process.exitCode = 1;
    }
    return;
  }
  throw new Error("Expected --describe, --evaluate <json>, or --assert.");
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  main(process.argv.slice(2));
}
