import { expect, test, type Page } from "@playwright/test";

import type { ApiResponseOfLocalAutomationScenarioResponse } from "@/src/lib/api/generated";
import {
  cleanupLocalAutomationUser,
  getGeneratedAccountSessions,
  getGeneratedAuthSession,
  signInLocalAutomationUser,
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

test("revoke one invalidates only the selected browser and returns a safe session projection", async ({
  browser,
  page,
}) => {
  const scenario = await createLocalAccount(page);
  const other = await browser.newContext();

  try {
    await signInLocalAutomationUser(
      other.request,
      scenario.email,
      scenario.password,
    );

    const sessions = await getGeneratedAccountSessions(page.context().request);
    expect(sessions.items).toHaveLength(2);
    for (const session of sessions.items) {
      expect(Object.keys(session).sort()).toEqual([
        "authenticationMethod",
        "createdAt",
        "expiresAt",
        "id",
        "ipAddress",
        "isCurrent",
        "lastSeenAt",
        "userAgent",
      ]);
      expect(JSON.stringify(session)).not.toMatch(
        /cookie|password|protected|secret|ticket|token/i,
      );
    }

    await page.goto("/user/security");
    await page.getByRole("button", { name: "Revoke session" }).click();
    await expect(page.getByRole("status")).toHaveText("Session revoked.");

    expect((await getGeneratedAuthSession(other.request)).authenticated).toBe(
      false,
    );
    expect(
      (await getGeneratedAuthSession(page.context().request)).authenticated,
    ).toBe(true);
  } finally {
    await other.close();
    await cleanupLocalAutomationUser(page.context().request);
  }
});

test("revoke all others preserves the current browser", async ({
  browser,
  page,
}) => {
  const scenario = await createLocalAccount(page);
  const other = await browser.newContext();

  try {
    await signInLocalAutomationUser(
      other.request,
      scenario.email,
      scenario.password,
    );
    await page.goto("/user/security");
    await page
      .getByRole("button", { name: "Revoke all other sessions" })
      .click();
    await expect(page.getByRole("status")).toHaveText(
      "1 other session revoked.",
    );

    expect((await getGeneratedAuthSession(other.request)).authenticated).toBe(
      false,
    );
    expect(
      (await getGeneratedAuthSession(page.context().request)).authenticated,
    ).toBe(true);

    await page.reload();
    await expect(page).toHaveURL(/\/user\/security$/);
    await expect(page.getByRole("heading", { name: "Security" })).toBeVisible();
  } finally {
    await other.close();
    await cleanupLocalAutomationUser(page.context().request);
  }
});
