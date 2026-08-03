import { expect, test } from "@playwright/test";

import type { ApiResponseOfLocalAutomationScenarioResponse } from "@/src/lib/api/generated";
import {
  waitForInteraction,
  waitForNavigationReady,
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
  test.setTimeout(90_000);
  await page.goto("/");
  await page.getByRole("link", { name: "Get Started" }).click();
  await waitForNavigationReady(
    page,
    /\/auth\/login\?redirect=%2Fdashboard$/,
    page.getByRole("button", { name: "Create local automation user" }),
  );
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

  await waitForNavigationReady(
    page,
    /\/welcome$/,
    page.getByRole("heading", { name: "Create your first workspace" }),
  );
  await expect(
    page.getByRole("heading", { name: "Create your first workspace" }),
  ).toBeVisible();
  const firstSessionId = (await getGeneratedAuthSession(page.context().request))
    .session?.id;
  expect(firstSessionId).toBeTruthy();
  await expect(page.locator("body")).not.toContainText(scenario.data.password);

  await page.reload();
  await waitForNavigationReady(
    page,
    /\/welcome$/,
    page.getByRole("heading", { name: "Create your first workspace" }),
  );
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
  await waitForNavigationReady(
    page,
    /\/w\/authentication-workspace\/dashboard$/,
    page.getByRole("region", { name: "Dashboard metrics" }),
  );
  await expect(
    page
      .getByRole("region", { name: "Dashboard metrics" })
      .getByRole("article"),
  ).toHaveCount(4);
  await expect(page.getByText("Demo changes are not saved.")).toBeVisible();

  const secondContext = await browser.newContext();
  const secondPage = await secondContext.newPage();
  await signInLocalAutomationUser(
    secondContext.request,
    scenario.data.email,
    scenario.data.password,
  );
  await secondPage.goto("/dashboard");
  await waitForNavigationReady(
    secondPage,
    /\/w\/authentication-workspace\/dashboard$/,
    secondPage.getByRole("region", { name: "Dashboard metrics" }),
  );
  const secondSessionId = (await getGeneratedAuthSession(secondContext.request))
    .session?.id;
  expect(secondSessionId).toBeTruthy();
  expect(secondSessionId).not.toBe(firstSessionId);

  await page
    .getByRole("banner")
    .getByRole("button", { name: "Open sidebar" })
    .click();
  const accountSettings = page.getByRole("link", {
    name: new RegExp(scenario.data.user.name),
  });
  await expect(accountSettings).toBeVisible();
  await accountSettings.click();
  await waitForNavigationReady(
    page,
    /\/user\/profile$/,
    page.getByRole("link", { name: "Profile", exact: true }),
  );
  const logout = page
    .locator("[data-slot='settings-page-shell']")
    .getByRole("button", { name: "Log out" });
  await waitForInteraction(logout);
  await logout.click();
  await waitForNavigationReady(
    page,
    /\/auth\/login$/,
    page.getByRole("button", { name: "Create local automation user" }),
  );
  await page.goto("/dashboard");
  await waitForNavigationReady(
    page,
    /\/auth\/login\?redirect=%2Fdashboard$/,
    page.getByRole("button", { name: "Create local automation user" }),
  );
  await secondPage.reload();
  await waitForNavigationReady(
    secondPage,
    /\/w\/authentication-workspace\/dashboard$/,
    secondPage.getByRole("region", { name: "Dashboard metrics" }),
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
  await waitForNavigationReady(
    secondPage,
    /\/auth\/login\?redirect=%2Fdashboard$/,
    secondPage.getByRole("button", { name: "Create local automation user" }),
  );
  await secondContext.close();
});
