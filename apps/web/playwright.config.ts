import { defineConfig, devices } from "@playwright/test";

const apiOrigin = "http://127.0.0.1:5297";
const webOrigin = "http://127.0.0.1:3127";
const liveProviderSmokeEnabled = process.env.E2E_LIVE_PROVIDER_SMOKE === "1";

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
  ...(liveProviderSmokeEnabled
    ? { testMatch: "external-provider-smoke.spec.ts" }
    : {}),
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI ? "github" : "list",
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
        PUBLIC_DEFAULT_LOCALE: "en",
      },
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      url: webOrigin,
    },
  ],
});
