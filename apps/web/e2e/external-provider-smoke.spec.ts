import { expect, test } from "@playwright/test";

const liveSmokeEnabled = process.env.E2E_LIVE_PROVIDER_SMOKE === "1";

test.use({ screenshot: "off", trace: "off", video: "off" });

const providers = [
  {
    id: "google",
    configurationName: "Google",
    displayName: "Google",
    hosts: ["accounts.google.com"],
  },
  {
    id: "github",
    configurationName: "GitHub",
    displayName: "GitHub",
    hosts: ["github.com"],
  },
  {
    id: "gitlab",
    configurationName: "GitLab",
    displayName: "GitLab",
    hosts: ["gitlab.com"],
  },
  {
    id: "vk",
    configurationName: "Vk",
    displayName: "VK",
    hosts: ["id.vk.com"],
  },
  {
    id: "yandex",
    configurationName: "Yandex",
    displayName: "Yandex",
    hosts: ["oauth.yandex.ru"],
  },
] as const;

function hasCompleteProcessConfiguration(configurationName: string): boolean {
  const prefix = `ExternalAuthentication__Providers__${configurationName}`;
  return [`${prefix}__ClientId`, `${prefix}__ClientSecret`].every((name) => {
    const value = process.env[name];
    return Boolean(value && value === value.trim());
  });
}

test.describe("live external provider authorization screens", () => {
  for (const provider of providers) {
    test(`${provider.displayName} external provider reaches its official authorization host`, async ({
      page,
    }) => {
      test.setTimeout(60_000);
      test.skip(
        !liveSmokeEnabled,
        "Live provider smoke is opt-in; set E2E_LIVE_PROVIDER_SMOKE=1.",
      );
      test.skip(
        !hasCompleteProcessConfiguration(provider.configurationName),
        `${provider.displayName} live smoke skipped because its named ClientId/ClientSecret process settings are incomplete.`,
      );

      await page.goto("/auth/login");
      const button = page.getByRole("button", {
        name: `Continue with ${provider.displayName}`,
      });
      await expect(button).toBeEnabled();
      const officialHosts: string[] = [...provider.hosts];
      const officialNavigation = page.waitForURL(
        (url) => officialHosts.includes(url.hostname),
        { timeout: 30_000, waitUntil: "commit" },
      );

      try {
        await Promise.all([officialNavigation, button.click()]);
      } catch {
        throw new Error(
          `${provider.displayName} did not reach its official authorization host.`,
        );
      }
      expect(officialHosts).toContain(new URL(page.url()).hostname);
    });
  }
});
