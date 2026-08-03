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
  await expectRenewals(1);
  await expectDashboard(page);
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
  await expectRenewals(2);

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
  await expectRenewals(3);
  await expect(
    page
      .getByRole("navigation", { name: "Workspace", exact: true })
      .getByRole("link", { name: "Workspaces", exact: true }),
  ).toHaveAttribute("aria-current", "page");
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
  await expectRenewals(4);
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
  await expectRenewals(5);

  await page
    .getByRole("link", { name: new RegExp(desktopIdentity.name) })
    .click();
  await waitForNavigationReady(
    page,
    "/user/profile",
    page.getByRole("link", { name: "Profile", exact: true }),
  );
  await waitForApplicationShell(page);
  await expectRenewals(6);
  await expect(
    page.getByRole("link", { name: "Profile", exact: true }),
  ).toHaveAttribute("aria-current", "page");
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

  const theme = page.getByRole("button", { name: "Switch to dark theme" });
  await waitForInteraction(theme);
  await theme.click();
  await expect(page.locator("html")).toHaveClass(/\bdark\b/);
  await page.reload();
  await waitForApplicationShell(page);
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
