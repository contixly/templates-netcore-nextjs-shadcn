import { defineConfig, devices } from "@playwright/test";

import {
  canonicalVisualBaselineEnvironment,
  currentVisualBaselineEnvironment,
  isCanonicalVisualBaselineEnvironment,
} from "./scripts/visual-baseline-environment.mjs";

const apiOrigin = "http://127.0.0.1:5297";
const webOrigin = "http://127.0.0.1:3127";
const russianWebOrigin = "http://127.0.0.1:3128";
const mobileWebOrigin = "https://127.0.0.1:3129";
const mobileRussianWebOrigin = "https://127.0.0.1:3130";
const liveProviderSmokeEnabled = process.env.E2E_LIVE_PROVIDER_SMOKE === "1";
const canonicalVisualBaselineEnabled = isCanonicalVisualBaselineEnvironment(
  currentVisualBaselineEnvironment(),
);
const canonicalVisualBaselineMetadata = {
  visualBaselineOperatingSystem:
    canonicalVisualBaselineEnvironment.operatingSystem,
};

const providerConfigurations = [
  "Google",
  "GitHub",
  "GitLab",
  "Vk",
  "Yandex",
] as const;

function configuredProcessValue(name: string): string | undefined {
  const value = process.env[name];
  return value && value === value.trim() ? value : undefined;
}

function externalAuthenticationEnvironment(): Record<string, string> {
  if (!liveProviderSmokeEnabled) {
    return Object.fromEntries([
      ["ExternalAuthentication__PublicOrigin", webOrigin],
      ...providerConfigurations.flatMap((provider) => [
        [
          `ExternalAuthentication__Providers__${provider}__ClientId`,
          `e2e-fake-${provider.toLowerCase()}-client-id`,
        ],
        [
          `ExternalAuthentication__Providers__${provider}__ClientSecret`,
          `e2e-fake-${provider.toLowerCase()}-client-secret`,
        ],
      ]),
    ]);
  }

  const environment: Record<string, string> = {
    ExternalAuthentication__PublicOrigin:
      configuredProcessValue("ExternalAuthentication__PublicOrigin") ??
      webOrigin,
  };
  for (const provider of providerConfigurations) {
    const prefix = `ExternalAuthentication__Providers__${provider}`;
    const clientId = configuredProcessValue(`${prefix}__ClientId`);
    const clientSecret = configuredProcessValue(`${prefix}__ClientSecret`);
    if (clientId && clientSecret) {
      environment[`${prefix}__ClientId`] = clientId;
      environment[`${prefix}__ClientSecret`] = clientSecret;
    }
  }
  return environment;
}

export default defineConfig({
  testDir: "./e2e",
  snapshotPathTemplate:
    "{snapshotDir}/{testFileDir}/{testFileName}-snapshots/{arg}{-projectName}{ext}",
  ...(liveProviderSmokeEnabled
    ? { testMatch: "external-provider-smoke.spec.ts" }
    : {}),
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  expect: { timeout: 15_000 },
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI ? "github" : "list",
  projects: liveProviderSmokeEnabled
    ? [
        {
          name: "desktop-light",
          metadata: { russianBaseURL: russianWebOrigin },
          use: { ...devices["Desktop Chrome"], colorScheme: "light" },
        },
      ]
    : canonicalVisualBaselineEnabled
      ? [
          {
            name: "desktop-light",
            metadata: {
              ...canonicalVisualBaselineMetadata,
              russianBaseURL: russianWebOrigin,
            },
            use: { ...devices["Desktop Chrome"], colorScheme: "light" },
          },
          {
            name: "desktop-dark",
            metadata: {
              ...canonicalVisualBaselineMetadata,
              russianBaseURL: russianWebOrigin,
            },
            testMatch: "ui-reference-parity.spec.ts",
            use: { ...devices["Desktop Chrome"], colorScheme: "dark" },
          },
          {
            name: "mobile-light",
            metadata: {
              ...canonicalVisualBaselineMetadata,
              russianBaseURL: mobileRussianWebOrigin,
            },
            testMatch: "ui-reference-parity.spec.ts",
            use: {
              ...devices["iPhone 13"],
              baseURL: mobileWebOrigin,
              colorScheme: "light",
              ignoreHTTPSErrors: true,
            },
          },
          {
            name: "mobile-dark",
            metadata: {
              ...canonicalVisualBaselineMetadata,
              russianBaseURL: mobileRussianWebOrigin,
            },
            testMatch: "ui-reference-parity.spec.ts",
            use: {
              ...devices["iPhone 13"],
              baseURL: mobileWebOrigin,
              colorScheme: "dark",
              ignoreHTTPSErrors: true,
            },
          },
        ]
      : [
          {
            name: "desktop-light",
            metadata: { russianBaseURL: russianWebOrigin },
            testIgnore: "ui-reference-parity.spec.ts",
            use: { ...devices["Desktop Chrome"], colorScheme: "light" },
          },
        ],
  use: {
    ...devices["Desktop Chrome"],
    baseURL: webOrigin,
    colorScheme: "light",
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
  },
  webServer: [
    {
      command:
        "dotnet run --no-launch-profile --project ../api/tests/Template.E2EHost/Template.E2EHost.csproj",
      env: {
        ASPNETCORE_ENVIRONMENT: "Test",
        ASPNETCORE_URLS: apiOrigin,
        ...externalAuthenticationEnvironment(),
        Documents__DefaultLocale: "en",
        LocalAutomationAuth__Enabled: "true",
        LocalAutomationAuth__CreateRateLimitPerMinute: "200",
        LocalAutomationAuth__SignInRateLimitPerFiveMinutes: "200",
      },
      reuseExistingServer: false,
      timeout: 180_000,
      url: `${apiOrigin}/api/health/ready`,
    },
    {
      command: "npm run dev -- --hostname 127.0.0.1 --port 3127",
      env: {
        API_INTERNAL_BASE_URL: apiOrigin,
        API_PROXY_TARGET: apiOrigin,
        APP_PUBLIC_ORIGIN: webOrigin,
        PUBLIC_DEFAULT_LOCALE: "en",
      },
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      url: webOrigin,
    },
    ...(!liveProviderSmokeEnabled && canonicalVisualBaselineEnabled
      ? [
          {
            command:
              "node ./scripts/run-e2e-web-server.mjs --port 3128 --locale ru",
            env: {
              API_INTERNAL_BASE_URL: apiOrigin,
              API_PROXY_TARGET: apiOrigin,
              APP_PUBLIC_ORIGIN: russianWebOrigin,
              PUBLIC_DEFAULT_LOCALE: "ru",
            },
            reuseExistingServer: !process.env.CI,
            timeout: 120_000,
            url: russianWebOrigin,
          },
          {
            command:
              "node ./scripts/run-e2e-web-server.mjs --port 3129 --locale en --https",
            env: {
              API_INTERNAL_BASE_URL: apiOrigin,
              API_PROXY_TARGET: apiOrigin,
              APP_PUBLIC_ORIGIN: mobileWebOrigin,
              PUBLIC_DEFAULT_LOCALE: "en",
            },
            reuseExistingServer: !process.env.CI,
            timeout: 120_000,
            url: mobileWebOrigin,
            ignoreHTTPSErrors: true,
          },
          {
            command:
              "node ./scripts/run-e2e-web-server.mjs --port 3130 --locale ru --https",
            env: {
              API_INTERNAL_BASE_URL: apiOrigin,
              API_PROXY_TARGET: apiOrigin,
              APP_PUBLIC_ORIGIN: mobileRussianWebOrigin,
              PUBLIC_DEFAULT_LOCALE: "ru",
            },
            reuseExistingServer: !process.env.CI,
            timeout: 120_000,
            url: mobileRussianWebOrigin,
            ignoreHTTPSErrors: true,
          },
        ]
      : []),
  ],
});
