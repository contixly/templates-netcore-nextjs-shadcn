import type { Page } from "@playwright/test";

import {
  waitForApplicationShell,
  waitForInteraction,
  waitForNavigationReady,
  waitForOrganizationControlInteraction,
} from "./support/app-readiness";
import {
  findMobileTableContainmentIssues,
  findSensitiveShellDisclosures,
} from "./support/application-shell-evidence";
import { signInLocalAutomationUser } from "./support/generated-auth-api";
import {
  expect,
  test,
  type OrganizationTestIdentity,
} from "./support/organization-test-fixture";

const password = "E2E-Application-Shell-123!";
const desktopIdentity: OrganizationTestIdentity = {
  email: "local-agent+application-shell-desktop@local-agent.test",
  name: "E2E Application Shell Desktop",
  password,
};
const mobileIdentity: OrganizationTestIdentity = {
  email: "local-agent+application-shell-mobile@local-agent.test",
  name: "E2E Application Shell Mobile",
  password,
};

function isBrowserSessionRenewal(request: {
  headers(): Record<string, string>;
  method(): string;
  url(): string;
}) {
  return (
    request.method() === "GET" &&
    new URL(request.url()).pathname === "/api/v1/auth/session" &&
    request.headers()["x-template-session-renewal"] === undefined
  );
}

async function expectNoSensitiveShellText(page: Page, secret: string) {
  const text = await page.locator("body").innerText();
  expect(findSensitiveShellDisclosures(text, [secret])).toEqual([]);
}

async function expectDashboard(page: Page) {
  await expect(
    page.getByRole("heading", { name: "Workspace dashboard" }),
  ).toBeVisible();
  const metrics = page.getByRole("region", { name: "Dashboard metrics" });
  await expect(metrics.getByRole("article")).toHaveCount(4);
  await expect(page.getByText("Demo changes are not saved.")).toBeVisible();
}

async function expectSingleApplicationMain(page: Page) {
  const main = page.getByRole("main");
  await expect(main).toHaveCount(1);
  await expect(main).toHaveAttribute("id", "main-content");
}

async function exerciseDashboardInteractions(page: Page) {
  const table = page.getByRole("table", { name: "Sections" });
  const search = page.getByRole("textbox", { name: "Search sections" });

  await table.getByRole("checkbox", { name: "Select Introduction" }).click();
  await expect(page.getByText("1 of 68 row(s) selected.")).toBeVisible();

  await search.fill("Technical approach");
  await expect(
    table.getByText("Technical approach", { exact: true }),
  ).toBeVisible();
  await expect(table.getByText("Introduction", { exact: true })).toHaveCount(0);
  await expect(page.getByText("0 of 1 row(s) selected.")).toBeVisible();
  await page
    .getByRole("button", { name: "Move Technical approach down" })
    .click();
  await search.fill("");
  await expect(page.getByText("1 of 68 row(s) selected.")).toBeVisible();
  for (const [index, header] of [
    "Introduction",
    "Table of contents",
    "Executive summary",
    "Technical approach",
  ].entries()) {
    await expect(table.getByRole("row").nth(index + 1)).toContainText(header);
  }

  await page.getByRole("button", { name: "Go to next page" }).click();
  await expect(page.getByText("Page 2 of 7")).toBeVisible();
  await expect(
    table.getByText("Adaptive Communication Protocols"),
  ).toBeVisible();
  await page.getByRole("button", { name: "Go to previous page" }).click();
  await expect(page.getByText("Page 1 of 7")).toBeVisible();

  await page.getByRole("button", { name: "Columns" }).click();
  await page.getByRole("menuitemcheckbox", { name: "Type" }).click();
  await expect(table.getByRole("columnheader", { name: "Type" })).toHaveCount(
    0,
  );

  const dragIntroduction = page.getByRole("button", {
    name: "Drag Introduction to reorder",
  });
  await dragIntroduction.focus();
  await dragIntroduction.press("Space");
  await dragIntroduction.press("ArrowDown");
  await dragIntroduction.press("Space");
  const reorderedRows = table.getByRole("row");
  await expect(reorderedRows.nth(1)).toContainText("Table of contents");
  await expect(reorderedRows.nth(2)).toContainText("Introduction");

  await page.getByRole("button", { name: "Edit Introduction" }).click();
  const editedHeader = "Browser-edited introduction";
  await page.getByRole("textbox", { name: "Section title" }).fill(editedHeader);
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(table.getByText(editedHeader, { exact: true })).toBeVisible();
  await expect(
    page.getByText("Local demo change applied. Changes are not saved.", {
      exact: true,
    }),
  ).toBeVisible();
  await expect(page.getByText("Demo changes are not saved.")).toBeVisible();

  await page.reload();
  await waitForApplicationShell(page);
  await expectSingleApplicationMain(page);
  const resetTable = page.getByRole("table", { name: "Sections" });
  await expect(
    resetTable.getByText("Introduction", { exact: true }),
  ).toBeVisible();
  await expect(resetTable.getByText(editedHeader, { exact: true })).toHaveCount(
    0,
  );
  await expect(
    resetTable.getByRole("columnheader", { name: "Type" }),
  ).toBeVisible();
  await expect(page.getByText("0 of 68 row(s) selected.")).toBeVisible();
  await expect(resetTable.getByRole("row").nth(1)).toContainText(
    "Introduction",
  );
}

test("desktop landing and authenticated shell cover primary navigation", async ({
  organizationScenario,
  page,
}) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1440, height: 1100 });

  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "Build the product, not the plumbing" }),
  ).toBeVisible();
  await expect(
    page.getByRole("link", { name: "Read the documentation" }),
  ).toHaveAttribute("href", "/docs");
  await expect(page.getByRole("link", { name: "Get started" })).toHaveAttribute(
    "href",
    "/auth/login?redirect=%2Fdashboard",
  );

  const landingTheme = page.getByRole("button", {
    name: "Switch to dark theme",
  });
  await waitForInteraction(landingTheme);
  await landingTheme.click();
  await expect(page.locator("html")).toHaveClass(/\bdark\b/);
  await page.getByRole("link", { name: "Read the documentation" }).click();
  await waitForNavigationReady(
    page,
    "/docs",
    page.getByRole("heading", { level: 1, name: "Template documentation" }),
  );
  await page.goto("/");
  await page.getByRole("link", { name: "Get started" }).click();
  await waitForNavigationReady(
    page,
    "/auth/login?redirect=%2Fdashboard",
    page.getByRole("button", { name: "Create local automation user" }),
  );

  const cleanupContext = await organizationScenario.createContext(
    "desktop application shell cleanup",
  );
  const owner = await organizationScenario.createLocalUser(
    cleanupContext,
    desktopIdentity,
    "desktop application shell owner",
  );
  const first = await organizationScenario.createOrganization(
    owner,
    cleanupContext.request,
    "E2E Shell Alpha",
  );
  const second = await organizationScenario.createOrganization(
    owner,
    cleanupContext.request,
    "E2E Shell Beta",
  );
  await signInLocalAutomationUser(
    page.context().request,
    desktopIdentity.email,
    desktopIdentity.password,
  );

  let renewals = 0;
  page.on("request", (request) => {
    if (isBrowserSessionRenewal(request)) renewals += 1;
  });
  async function expectRenewals(expected: number) {
    await expect.poll(() => renewals).toBe(expected);
    await page.waitForTimeout(250);
    expect(renewals).toBe(expected);
  }

  await page.goto(`/w/${first.canonicalKey}/dashboard`);
  await waitForApplicationShell(page);
  await expectSingleApplicationMain(page);
  await expectRenewals(1);
  await expectDashboard(page);
  await exerciseDashboardInteractions(page);
  await expectRenewals(2);
  const expandSidebar = page
    .getByRole("banner")
    .getByRole("button", { name: "Open sidebar" });
  await expandSidebar.click();
  await expect(
    page.getByText(desktopIdentity.name, { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Current workspace: E2E Shell Alpha" }),
  ).toBeVisible();
  await expect(
    page
      .getByRole("navigation", { name: "Workspace", exact: true })
      .getByRole("link", { name: "Dashboard", exact: true }),
  ).toHaveAttribute("aria-current", "page");
  await expectNoSensitiveShellText(page, desktopIdentity.password);
  await page.screenshot({
    path: test.info().outputPath("desktop-shell.png"),
    fullPage: true,
  });

  const switcher = page.getByRole("button", {
    name: "Current workspace: E2E Shell Alpha",
  });
  await waitForOrganizationControlInteraction(switcher);
  await switcher.click();
  const switchResponse = page.waitForResponse((response) => {
    const request = response.request();
    return (
      request.method() === "PUT" &&
      new URL(response.url()).pathname ===
        "/api/v1/auth/session/active-organization"
    );
  });
  await page.getByRole("button", { name: "Switch to E2E Shell Beta" }).click();
  expect((await switchResponse).status()).toBe(200);
  await waitForNavigationReady(
    page,
    `/w/${second.canonicalKey}/dashboard`,
    page.getByRole("main").getByText(/E2E Shell Beta/),
  );
  await waitForApplicationShell(page);
  await expectSingleApplicationMain(page);
  await expectRenewals(3);

  await page
    .getByRole("navigation", { name: "Workspace", exact: true })
    .getByRole("link", { name: "Workspaces", exact: true })
    .click();
  await waitForNavigationReady(
    page,
    "/workspaces",
    page.getByRole("article", { name: "E2E Shell Beta workspace" }),
  );
  await waitForApplicationShell(page);
  await expectSingleApplicationMain(page);
  await expectRenewals(4);
  await expect(
    page
      .getByRole("navigation", { name: "Workspace", exact: true })
      .getByRole("link", { name: "Workspaces", exact: true }),
  ).toHaveAttribute("aria-current", "page");
  await expect(
    page.getByRole("button", { name: "Current workspace: E2E Shell Beta" }),
  ).toBeVisible();
  await expectNoSensitiveShellText(page, desktopIdentity.password);

  await page
    .getByRole("article", { name: "E2E Shell Beta workspace" })
    .getByRole("link", { name: "Settings" })
    .click();
  await waitForNavigationReady(
    page,
    `/w/${second.canonicalKey}/settings/workspace`,
    page.getByRole("navigation", { name: "Workspace settings" }),
  );
  await waitForApplicationShell(page);
  await expectSingleApplicationMain(page);
  await expectRenewals(5);
  await expect(
    page.getByRole("navigation", { name: "Workspace settings" }),
  ).toBeVisible();
  await expect(
    page.getByRole("link", { name: "Workspace", exact: true }),
  ).toHaveAttribute("aria-current", "page");
  await expectNoSensitiveShellText(page, desktopIdentity.password);

  const docsShortcut = page
    .getByRole("banner")
    .getByRole("link", { name: "Documentation" });
  await expect(docsShortcut).toHaveAttribute("href", "/docs");
  await docsShortcut.click();
  await waitForNavigationReady(
    page,
    "/docs",
    page.getByRole("heading", { level: 1, name: "Template documentation" }),
  );
  await page.goto(`/w/${second.canonicalKey}/settings/workspace`);
  await waitForApplicationShell(page);
  await expectSingleApplicationMain(page);
  await expectRenewals(6);

  await page
    .getByRole("link", { name: new RegExp(desktopIdentity.name) })
    .click();
  await waitForNavigationReady(
    page,
    "/user/profile",
    page.getByRole("link", { name: "Profile", exact: true }),
  );
  await waitForApplicationShell(page);
  await expectSingleApplicationMain(page);
  await expectRenewals(7);
  await expect(
    page.getByRole("link", { name: "Profile", exact: true }),
  ).toHaveAttribute("aria-current", "page");
  await expect(
    page.getByRole("button", { name: "Current workspace: E2E Shell Beta" }),
  ).toBeVisible();
  await expectNoSensitiveShellText(page, desktopIdentity.password);

  const logout = page
    .locator("[data-slot='settings-page-shell']")
    .getByRole("button", { name: "Log out" });
  await waitForInteraction(logout);
  await logout.click();
  await waitForNavigationReady(
    page,
    "/auth/login",
    page.getByRole("button", { name: "Create local automation user" }),
  );
});

test("mobile shell drawer, dashboard, theme, and settings stay responsive", async ({
  organizationScenario,
  page,
}) => {
  test.setTimeout(90_000);
  const hydrationErrors: string[] = [];
  page.on("console", (message) => {
    if (message.text().includes("A tree hydrated but some attributes")) {
      hydrationErrors.push(message.text());
    }
  });
  await page.setViewportSize({ width: 390, height: 844 });
  const owner = await organizationScenario.createLocalUser(
    page.context(),
    mobileIdentity,
    "mobile application shell owner",
  );
  const first = await organizationScenario.createOrganization(
    owner,
    page.context().request,
    "E2E Mobile Alpha",
  );
  const second = await organizationScenario.createOrganization(
    owner,
    page.context().request,
    "E2E Mobile Beta",
  );

  await page.goto(`/w/${first.canonicalKey}/dashboard`);
  await waitForApplicationShell(page);
  await expectSingleApplicationMain(page);
  await expectDashboard(page);
  await expect(
    page.getByRole("dialog", { name: "Application navigation" }),
  ).toHaveCount(0);

  const openSidebar = page.getByRole("button", { name: "Open sidebar" });
  await openSidebar.click();
  const drawer = page.getByRole("dialog", { name: "Application navigation" });
  await expect(drawer).toBeVisible();
  await drawer.getByRole("button", { name: "Close sidebar" }).click();
  await expect(drawer).toHaveCount(0);

  await openSidebar.click();
  const switcher = drawer.getByRole("button", {
    name: "Current workspace: E2E Mobile Alpha",
  });
  await waitForOrganizationControlInteraction(switcher);
  await switcher.click();
  await page.getByRole("button", { name: "Switch to E2E Mobile Beta" }).click();
  await waitForNavigationReady(
    page,
    `/w/${second.canonicalKey}/dashboard`,
    page.getByRole("main").getByText(/E2E Mobile Beta/),
  );
  await expect(drawer).toHaveCount(0);
  await waitForApplicationShell(page);
  await expectSingleApplicationMain(page);

  const theme = page.getByRole("button", { name: "Switch to dark theme" });
  await waitForInteraction(theme);
  await theme.click();
  await expect(page.locator("html")).toHaveClass(/\bdark\b/);
  await page.reload();
  await waitForApplicationShell(page);
  await expectSingleApplicationMain(page);
  await expect(page.locator("html")).toHaveClass(/\bdark\b/);

  await expect(
    page.getByRole("combobox", { name: "Total visitors" }),
  ).toContainText("Last 7 days");
  await expect(page.getByTestId("activity-chart-point")).toHaveCount(7);
  const tableContainer = page.locator("[data-slot='table-container']");
  const containment = await tableContainer.evaluate((element) => {
    const table = element.querySelector("table");
    if (!(table instanceof HTMLTableElement)) {
      throw new Error("Activity table was not rendered inside its container.");
    }
    const containerBounds = element.getBoundingClientRect();
    const tableBounds = table.getBoundingClientRect();
    return {
      clientWidth: element.clientWidth,
      containerLeft: containerBounds.left,
      containerRight: containerBounds.right,
      overflowX: getComputedStyle(element).overflowX,
      scrollWidth: element.scrollWidth,
      tableLeft: tableBounds.left,
      tableRight: tableBounds.right,
      tableWidth: tableBounds.width,
      viewportWidth: window.innerWidth,
    };
  });
  expect(findMobileTableContainmentIssues(containment)).toEqual([]);
  await expectNoSensitiveShellText(page, mobileIdentity.password);
  await page.screenshot({
    path: test.info().outputPath("mobile-shell.png"),
    fullPage: true,
  });

  await page.getByRole("button", { name: "Open sidebar" }).click();
  await drawer.getByRole("link", { name: "Workspaces", exact: true }).click();
  await waitForNavigationReady(
    page,
    "/workspaces",
    page.getByRole("article", { name: "E2E Mobile Beta workspace" }),
  );
  await expect(drawer).toHaveCount(0);
  await waitForApplicationShell(page);
  await expectSingleApplicationMain(page);
  await expectNoSensitiveShellText(page, mobileIdentity.password);
  await page
    .getByRole("article", { name: "E2E Mobile Beta workspace" })
    .getByRole("link", { name: "Settings" })
    .click();
  await waitForNavigationReady(
    page,
    `/w/${second.canonicalKey}/settings/workspace`,
    page.getByRole("navigation", { name: "Workspace settings" }),
  );
  await waitForApplicationShell(page);
  await expect(
    page.getByRole("navigation", { name: "Workspace settings" }),
  ).toBeVisible();
  await expect(
    page.getByRole("link", { name: "Workspace", exact: true }),
  ).toHaveAttribute("aria-current", "page");
  await expectNoSensitiveShellText(page, mobileIdentity.password);
  expect(hydrationErrors).toEqual([]);
});
