import { expect, test } from "@playwright/test";

import type { ApiResponseOfLocalAutomationScenarioResponse } from "@/src/lib/api/generated";
import {
  waitForInteraction,
  waitForOrganizationControlInteraction,
} from "./support/app-readiness";
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
  const createLocalUser = page.getByRole("button", {
    name: "Create local automation user",
  });
  await waitForInteraction(createLocalUser);
  await createLocalUser.click();
  const scenario = (await (
    await scenarioResponse
  ).json()) as ApiResponseOfLocalAutomationScenarioResponse;

  await expect(page).toHaveURL(/\/welcome$/);
  await expect(
    page.getByRole("heading", { name: "Create your first workspace" }),
  ).toBeVisible();
  const firstSessionId = (await getGeneratedAuthSession(page.context().request))
    .session?.id;
  expect(firstSessionId).toBeTruthy();
  await expect(page.locator("body")).not.toContainText(scenario.data.password);

  await page.reload();
  await expect(page).toHaveURL(/\/welcome$/);
  expect(
    (await getGeneratedAuthSession(page.context().request)).session?.id,
  ).toBe(firstSessionId);

  const createWorkspace = page.getByRole("button", {
    name: "Create Workspace",
  });
  await waitForOrganizationControlInteraction(createWorkspace);
  await createWorkspace.click();
  await page.getByLabel(/Workspace name/i).fill("Authentication Workspace");
  const workspaceResponse = page.waitForResponse((response) => {
    const request = response.request();
    return (
      request.method() === "POST" &&
      new URL(response.url()).pathname === "/api/v1/organizations"
    );
  });
  await page.getByRole("button", { name: "Create", exact: true }).click();
  expect((await workspaceResponse).status()).toBe(201);
  await expect(page).toHaveURL(/\/w\/authentication-workspace\/dashboard$/);

  const secondContext = await browser.newContext();
  const secondPage = await secondContext.newPage();
  await signInLocalAutomationUser(
    secondContext.request,
    scenario.data.email,
    scenario.data.password,
  );
  await secondPage.goto("/dashboard");
  await expect(secondPage).toHaveURL(
    /\/w\/authentication-workspace\/dashboard$/,
  );
  const secondSessionId = (await getGeneratedAuthSession(secondContext.request))
    .session?.id;
  expect(secondSessionId).toBeTruthy();
  expect(secondSessionId).not.toBe(firstSessionId);

  const accountSettings = page.getByRole("link", {
    name: "Account settings",
  });
  await expect(accountSettings).toBeVisible();
  await accountSettings.click();
  await expect(page).toHaveURL(/\/user\/profile$/);
  const logout = page.getByRole("button", { name: "Log out" });
  await waitForInteraction(logout);
  await logout.click();
  await expect(page).toHaveURL(/\/auth\/login$/);
  await page.goto("/dashboard");
  await expect(page).toHaveURL(/\/auth\/login\?redirect=%2Fdashboard$/);
  await secondPage.reload();
  await expect(secondPage).toHaveURL(
    /\/w\/authentication-workspace\/dashboard$/,
  );
  expect(
    (await getGeneratedAuthSession(secondContext.request)).session?.id,
  ).toBe(secondSessionId);

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
