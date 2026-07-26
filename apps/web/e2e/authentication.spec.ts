import { expect, test } from "@playwright/test";

import type { ApiResponseOfLocalAutomationScenarioResponse } from "@/src/lib/api/generated";
import {
  cleanupLocalAutomationUser,
  getGeneratedAuthSession,
  signInLocalAutomationUser,
} from "./support/generated-auth-api";

test("local credentials create persistent independent sessions and cleanup all access", async ({
  browser,
  page,
}) => {
  await page.goto("/");
  await page.getByRole("link", { name: "Get Started" }).click();
  await expect(page).toHaveURL(/\/auth\/login\?redirect=%2Fdashboard$/);
  await expect(
    page.getByRole("button", { name: "Create local automation user" }),
  ).toBeVisible();

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
  const scenario = (await (
    await scenarioResponse
  ).json()) as ApiResponseOfLocalAutomationScenarioResponse;

  await expect(page).toHaveURL(/\/dashboard$/);
  const firstSessionId = await page.getByTestId("session-id").textContent();
  expect(firstSessionId).toBeTruthy();
  await expect(page.locator("body")).not.toContainText(scenario.data.password);

  await page.reload();
  await expect(page).toHaveURL(/\/dashboard$/);
  await expect(page.getByTestId("session-id")).toHaveText(firstSessionId!);

  const secondContext = await browser.newContext();
  const secondPage = await secondContext.newPage();
  await signInLocalAutomationUser(
    secondContext.request,
    scenario.data.email,
    scenario.data.password,
  );
  await secondPage.goto("/dashboard");
  const secondSessionId = await secondPage
    .getByTestId("session-id")
    .textContent();
  expect(secondSessionId).toBeTruthy();
  expect(secondSessionId).not.toBe(firstSessionId);

  await page.getByRole("button", { name: "Log out" }).click();
  await expect(page).toHaveURL(/\/auth\/login$/);
  await secondPage.reload();
  await expect(secondPage).toHaveURL(/\/dashboard$/);
  await expect(secondPage.getByTestId("session-id")).toHaveText(
    secondSessionId!,
  );

  await cleanupLocalAutomationUser(secondContext.request);
  expect(
    (await getGeneratedAuthSession(secondContext.request)).authenticated,
  ).toBe(false);
  expect(
    (await getGeneratedAuthSession(page.context().request)).authenticated,
  ).toBe(false);

  await secondPage.goto("/dashboard");
  await expect(secondPage).toHaveURL(/\/auth\/login\?redirect=%2Fdashboard$/);
  await secondContext.close();
});
