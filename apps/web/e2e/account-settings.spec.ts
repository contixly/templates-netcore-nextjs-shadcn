import { expect, test, type Page } from "@playwright/test";

import type { ApiResponseOfLocalAutomationScenarioResponse } from "@/src/lib/api/generated";
import { waitForAppHydration } from "./support/app-readiness";
import {
  cleanupLocalAutomationUser,
  createLocalAutomationUser,
  getGeneratedAccount,
  getGeneratedAuthSession,
} from "./support/generated-auth-api";

async function createLocalAccount(
  page: Page,
): Promise<ApiResponseOfLocalAutomationScenarioResponse["data"]> {
  await page.goto("/auth/login");
  await waitForAppHydration(page);
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
    await waitForAppHydration(page);
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
  await waitForAppHydration(page);

  for (const provider of ["Google", "GitHub", "GitLab", "VK", "Yandex"]) {
    await expect(
      page.getByRole("button", { name: `Continue with ${provider}` }),
    ).toBeEnabled();
  }

  await createLocalAccount(page);
  try {
    await page.goto("/user/connections");
    await waitForAppHydration(page);

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

test("account deletion rejects a mismatched confirmation and does not reuse the deleted identity", async ({
  page,
}) => {
  const scenario = await createLocalAccount(page);

  try {
    const oldAccount = await getGeneratedAccount(page.context().request);
    expect(oldAccount.id).toBe(scenario.user.id);

    await page.goto("/user/danger");
    await waitForAppHydration(page);
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

    let resolveDeletionResponse:
      ((value: { body: unknown; status: number }) => void) | undefined;
    const deletionResponsePromise = new Promise<{
      body: unknown;
      status: number;
    }>((resolve) => {
      resolveDeletionResponse = resolve;
    });
    await page.route(
      "http://127.0.0.1:3127/api/v1/account",
      async (route, request) => {
        if (request.method() !== "DELETE") {
          await route.continue();
          return;
        }
        const response = await route.fetch();
        resolveDeletionResponse?.({
          body: await response.json(),
          status: response.status(),
        });
        await route.fulfill({ response });
      },
    );
    await submit.click();

    const deletionResponse = await deletionResponsePromise;
    expect(deletionResponse.status).toBe(200);
    expect(deletionResponse.body).toEqual({
      data: { deleted: true },
    });

    await page.waitForURL((url) => {
      return (
        url.origin === "http://127.0.0.1:3127" &&
        url.pathname === "/" &&
        url.search === "" &&
        url.hash === ""
      );
    });
    const finalUrl = new URL(page.url());
    expect(finalUrl.origin).toBe("http://127.0.0.1:3127");
    expect(finalUrl.pathname).toBe("/");
    expect(finalUrl.search).toBe("");
    expect(finalUrl.hash).toBe("");

    expect(
      (await getGeneratedAuthSession(page.context().request)).authenticated,
    ).toBe(false);
    expect(
      (await page.context().request.storageState()).cookies.filter(
        (cookie) => cookie.name === "__Host-template.session",
      ),
    ).toEqual([]);

    const replacement = await createLocalAutomationUser(
      page.context().request,
      {
        name: "Replacement account",
        email: scenario.email,
        password: scenario.password,
      },
    );
    expect(replacement.user.id).not.toBe(oldAccount.id);
    expect((await getGeneratedAccount(page.context().request)).id).toBe(
      replacement.user.id,
    );
  } finally {
    const currentSession = await getGeneratedAuthSession(
      page.context().request,
    );
    if (currentSession.authenticated) {
      await cleanupLocalAutomationUser(page.context().request);
    }
  }
});
