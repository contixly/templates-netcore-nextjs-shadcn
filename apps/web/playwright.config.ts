import { defineConfig, devices } from "@playwright/test";

const apiOrigin = "http://127.0.0.1:5297";
const webOrigin = "http://127.0.0.1:3127";

export default defineConfig({
  testDir: "./e2e",
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
        LocalAutomationAuth__Enabled: "true",
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
