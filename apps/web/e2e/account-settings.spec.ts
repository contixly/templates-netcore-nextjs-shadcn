import { expect, test, type Page } from "@playwright/test";

import type { ApiResponseOfLocalAutomationScenarioResponse } from "@/src/lib/api/generated";
import {
  cleanupLocalAutomationUser,
  getGeneratedAuthSession,
} from "./support/generated-auth-api";

async function createLocalAccount(
  page: Page,
): Promise<ApiResponseOfLocalAutomationScenarioResponse["data"]> {
  await page.goto("/auth/login");
  const scenarioResponse = page.waitForResponse((response) => {
    const request = response.request();
    return (
      request.method() === "POST" &&
      new URL(response.url()).pathname === "/api/local-auth/scenario"
    );
  });
  await page
    .getByRole("button", { name: "Create local automation user" })
    .click();
  const response = await scenarioResponse;
  expect(response.ok()).toBe(true);
  return (
    (await response.json()) as ApiResponseOfLocalAutomationScenarioResponse
  ).data;
}

test("account root redirects to a persisted profile update", async ({
  page,
}) => {
  await createLocalAccount(page);

  try {
    await page.goto("/user");
    await expect(page).toHaveURL(/\/user\/profile$/);
    await expect(
      page.getByRole("heading", { name: "Profile settings" }),
    ).toBeVisible();

    const displayName = page.getByRole("textbox", { name: "Display name" });
    await displayName.fill("Browser Account");
    await page.getByRole("button", { name: "Save profile" }).click();
    await expect(page.getByRole("status")).toHaveText("Profile updated.");
    await expect(displayName).toHaveValue("Browser Account");

    await page.reload();
    await expect(displayName).toHaveValue("Browser Account");
  } finally {
    await cleanupLocalAutomationUser(page.context().request);
  }
});

test("configured external providers are available in login and account states", async ({
  page,
}) => {
  await page.goto("/auth/login");

  for (const provider of ["Google", "GitHub", "GitLab", "VK", "Yandex"]) {
    await expect(
      page.getByRole("button", { name: `Continue with ${provider}` }),
    ).toBeEnabled();
  }

  await createLocalAccount(page);
  try {
    await page.goto("/user/connections");

    for (const provider of ["Google", "GitHub", "GitLab", "VK", "Yandex"]) {
      const connection = page.getByRole("article", {
        name: `${provider} connection`,
      });
      await expect(connection).toContainText("Not connected");
      await expect(
        connection.getByRole("button", { name: `Connect ${provider}` }),
      ).toBeEnabled();
      await expect(connection).not.toContainText(
        "Provider configuration is unavailable",
      );
    }
  } finally {
    await cleanupLocalAutomationUser(page.context().request);
  }
});

test("account deletion rejects a mismatched confirmation and clears access on success", async ({
  page,
}) => {
  const scenario = await createLocalAccount(page);
  await page.goto("/user/danger");
  await page.getByRole("button", { name: "Delete account" }).click();

  const confirmation = page.getByRole("textbox", {
    name: `Type ${scenario.email} to confirm`,
  });
  const submit = page.getByRole("button", {
    name: "Permanently delete account",
  });

  await confirmation.fill(`wrong-${scenario.email}`);
  await expect(submit).toBeDisabled();
  expect(
    (await getGeneratedAuthSession(page.context().request)).authenticated,
  ).toBe(true);

  await confirmation.fill(scenario.email);
  await expect(submit).toBeEnabled();
  await submit.click();

  await expect(page).toHaveURL(/\/$/);
  expect(
    (await getGeneratedAuthSession(page.context().request)).authenticated,
  ).toBe(false);
});
