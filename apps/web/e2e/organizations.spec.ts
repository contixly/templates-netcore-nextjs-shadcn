import type { Page } from "@playwright/test";

import {
  waitForInteraction,
  waitForOrganizationControlInteraction,
} from "./support/app-readiness";
import { getGeneratedOrganizations } from "./support/generated-organizations-api";
import {
  expect,
  test,
  type TrackedLocalAutomationScenario,
  type OrganizationTestIdentity,
} from "./support/organization-test-fixture";
import type { OrganizationDetailResponse } from "@/src/lib/api/generated";

const localPassword = "E2E-Organization-Owner-123!";

const onboardingOwner: OrganizationTestIdentity = {
  name: "E2E Organization Owner",
  email: "local-agent+organization-onboarding-owner@local-agent.test",
  password: localPassword,
};

const membershipOwner: OrganizationTestIdentity = {
  name: "E2E Membership Owner",
  email: "local-agent+organization-membership-owner@local-agent.test",
  password: localPassword,
};

const membershipMember: OrganizationTestIdentity = {
  name: "E2E Organization Member",
  email: "local-agent+organization-membership-member@local-agent.test",
  password: localPassword,
};

const membershipOutsider: OrganizationTestIdentity = {
  name: "E2E Organization Outsider",
  email: "local-agent+organization-membership-outsider@local-agent.test",
  password: localPassword,
};

const slugOwner: OrganizationTestIdentity = {
  name: "E2E Slug Owner",
  email: "local-agent+organization-slug-owner@local-agent.test",
  password: localPassword,
};

const staleSettingsOwner: OrganizationTestIdentity = {
  name: "E2E Stale Settings Owner",
  email: "local-agent+organization-stale-settings-owner@local-agent.test",
  password: localPassword,
};

const staleSettingsAdmin: OrganizationTestIdentity = {
  name: "E2E Stale Settings Admin",
  email: "local-agent+organization-stale-settings-admin@local-agent.test",
  password: localPassword,
};

const paginationOwner: OrganizationTestIdentity = {
  name: "E2E Pagination Owner",
  email: "local-agent+organization-pagination-owner@local-agent.test",
  password: localPassword,
};

async function expectOrganizationSettingsLinks(page: Page) {
  for (const collaborationSurface of [/invitations?/i, /teams?/i]) {
    await expect(
      page.getByRole("link", { name: collaborationSurface }),
    ).toBeVisible();
  }
  await expect(page.getByRole("link", { name: /api keys?/i })).toBeVisible();
}

function isOrganizationCreateResponse(url: string, method: string) {
  return method === "POST" && new URL(url).pathname === "/api/v1/organizations";
}

async function createWorkspaceThroughBrowser(
  page: Page,
  organizationScenario: {
    organizationCreated: (
      scenario: TrackedLocalAutomationScenario,
      organizationId?: string,
    ) => void;
  },
  owner: TrackedLocalAutomationScenario,
  triggerName: "Create New Workspace" | "Create Workspace",
  name: string,
): Promise<OrganizationDetailResponse> {
  const trigger = page.getByRole("button", { name: triggerName });
  await waitForOrganizationControlInteraction(trigger);
  await trigger.click();
  await page.getByLabel(/Workspace name/i).fill(name);

  const persistedOrganization = page.waitForResponse((response) => {
    const request = response.request();
    return isOrganizationCreateResponse(response.url(), request.method());
  });
  await page.getByRole("button", { name: "Create", exact: true }).click();
  const createResponse = await persistedOrganization;
  expect(createResponse.status()).toBe(201);
  const body = (await createResponse.json()) as {
    data: OrganizationDetailResponse;
  };
  organizationScenario.organizationCreated(owner, body.data.id);
  return body.data;
}

test.describe.serial("organization full-stack workflows", () => {
  test("zero-organization routes, browser creation, and active fallback routing", async ({
    organizationScenario,
    page,
  }) => {
    test.setTimeout(60_000);
    const owner = await organizationScenario.createLocalUser(
      page.context(),
      onboardingOwner,
      "onboarding owner",
    );
    let browserSessionReads = 0;
    page.on("request", (request) => {
      if (
        request.method() === "GET" &&
        new URL(request.url()).pathname === "/api/v1/auth/session"
      ) {
        expect(request.headers()["x-template-session-renewal"]).toBeUndefined();
        browserSessionReads += 1;
      }
    });
    async function expectBrowserSessionReads(expected: number) {
      await expect.poll(() => browserSessionReads).toBe(expected);
      await page.waitForTimeout(250);
      expect(browserSessionReads).toBe(expected);
    }

    await page.goto("/dashboard");
    await expect(page).toHaveURL("/welcome");
    await expect(
      page.getByRole("heading", { name: "Create your first workspace" }),
    ).toBeVisible();
    await expectBrowserSessionReads(1);
    await page.goto("/workspaces");
    await expect(
      page.getByRole("heading", { name: "Workspaces", exact: true }),
    ).toBeVisible();
    await expect(
      page.getByRole("heading", { name: "No workspaces yet" }),
    ).toBeVisible();
    await expectBrowserSessionReads(2);
    await waitForOrganizationControlInteraction(
      page.getByRole("button", { name: "Create New Workspace" }),
    );

    for (const route of [
      ["/user/profile", "Profile settings"],
      ["/user/connections", "Connections"],
      ["/user/security", "Security"],
      ["/user/danger", "Danger zone"],
    ] as const) {
      await page.goto(route[0]);
      await expect(page.getByRole("heading", { name: route[1] })).toBeVisible();
    }
    await expectBrowserSessionReads(6);

    await page.goto("/dashboard");
    await expect(page).toHaveURL("/welcome");
    const first = await createWorkspaceThroughBrowser(
      page,
      organizationScenario,
      owner,
      "Create Workspace",
      "E2E Organization",
    );
    await expect(page).toHaveURL("/w/e2e-organization/dashboard");

    await page.waitForTimeout(250);
    browserSessionReads = 0;
    const switcher = page.getByRole("button", {
      name: "Current workspace: E2E Organization",
    });
    await waitForOrganizationControlInteraction(switcher);
    await switcher.click();
    await page.getByRole("link", { name: "Manage workspaces" }).click();
    await expect(page).toHaveURL("/workspaces");
    await expect(
      page.getByRole("heading", { name: "Workspaces", exact: true }),
    ).toBeVisible();
    await expectBrowserSessionReads(1);

    await page
      .getByRole("article", { name: "E2E Organization workspace" })
      .getByRole("link", { name: "Settings" })
      .click();
    await expect(page).toHaveURL("/w/e2e-organization/settings/workspace");
    await expect(
      page.getByRole("heading", { name: "Workspace settings" }),
    ).toBeVisible();
    await expectBrowserSessionReads(2);

    await page.getByRole("link", { name: "Users", exact: true }).click();
    await expect(page).toHaveURL("/w/e2e-organization/settings/users");
    await expect(
      page.getByRole("heading", { name: "Workspace users" }),
    ).toBeVisible();
    await expectBrowserSessionReads(3);

    await page.getByRole("link", { name: "Account settings" }).click();
    await expect(page).toHaveURL("/user/profile");
    await expect(
      page.getByRole("heading", { name: "Profile settings" }),
    ).toBeVisible();
    await expectBrowserSessionReads(4);

    for (const [linkName, route, heading, expectedReads] of [
      ["Connections", "/user/connections", "Connections", 5],
      ["Security", "/user/security", "Security", 6],
      ["Danger", "/user/danger", "Danger zone", 7],
    ] as const) {
      await page.getByRole("link", { name: linkName, exact: true }).click();
      await expect(page).toHaveURL(route);
      await expect(
        page.getByRole("heading", { name: heading, exact: true }),
      ).toBeVisible();
      await expectBrowserSessionReads(expectedReads);
    }

    await page.goto("/workspaces");
    await expect(
      page.getByRole("article", { name: "E2E Organization workspace" }),
    ).toBeVisible();
    const second = await createWorkspaceThroughBrowser(
      page,
      organizationScenario,
      owner,
      "Create New Workspace",
      "Z E2E Active Organization",
    );
    await expect(page).toHaveURL("/w/z-e2e-active-organization/dashboard");

    await page.goto("/dashboard");
    await expect(page).toHaveURL("/w/z-e2e-active-organization/dashboard");

    expect(
      await organizationScenario.deleteOrganization(
        owner,
        page.context().request,
        second,
      ),
    ).toBe(second.id);
    await page.goto("/dashboard");
    await expect(page).toHaveURL("/w/e2e-organization/dashboard");

    const organizations = await getGeneratedOrganizations(
      page.context().request,
    );
    expect(organizations.items).toHaveLength(1);
    expect(organizations.items[0]).toMatchObject({
      name: "E2E Organization",
      slug: "e2e-organization",
      canonicalKey: "e2e-organization",
    });

    await page.goto(`/w/${organizations.items[0].id}`);
    await expect(page).toHaveURL("/w/e2e-organization/dashboard");
    expect(organizations.items[0].id).toBe(first.id);

    await page.goto("/w/e2e-organization/settings/workspace");
    await expect(
      page.getByRole("navigation", { name: "Workspace settings" }),
    ).toBeVisible();
    await expectOrganizationSettingsLinks(page);
  });

  test("owner adds an outside-domain member and member settings stay read-only", async ({
    organizationScenario,
    page,
  }) => {
    const preparedOwner = organizationScenario.prepareLocalUser(
      page.context(),
      membershipOwner,
      "membership owner",
    );
    const memberContext = await organizationScenario.createContext(
      "membership member context",
    );
    const memberPage = await memberContext.newPage();
    const outsiderContext = await organizationScenario.createContext(
      "membership outsider context",
    );
    const outsiderPage = await outsiderContext.newPage();
    const preparedMember = organizationScenario.prepareLocalUser(
      memberContext,
      membershipMember,
      "membership member",
    );
    const preparedOutsider = organizationScenario.prepareLocalUser(
      outsiderContext,
      membershipOutsider,
      "membership outsider",
    );
    const [owner, member, outsider] =
      await organizationScenario.createLocalUsers([
        preparedOwner,
        preparedMember,
        preparedOutsider,
      ]);
    expect(owner.user.id).not.toBe(member.user.id);
    expect(outsider.user.id).not.toBe(owner.user.id);

    const organization = await organizationScenario.createOrganization(
      owner,
      page.context().request,
      "E2E Membership Policy",
    );
    await organizationScenario.createOrganization(
      owner,
      page.context().request,
      "E2E Membership Safeguard",
    );
    await organizationScenario.createOrganization(
      outsider,
      outsiderContext.request,
      "E2E Outsider Home",
    );

    await page.goto(`/w/${organization.canonicalKey}/settings/workspace`);
    const workspaceName = page.getByRole("textbox", {
      name: "Workspace Name",
    });
    await waitForInteraction(workspaceName);
    await workspaceName.fill("E2E Membership Policy Renamed");
    const domains = page.getByRole("textbox", {
      name: "Allowed Email Domains",
    });
    await domains.fill("@Example.COM,\nexample.com,\nSUB.Example.COM");
    await expect(
      page.getByText("Normalized domains: example.com, sub.example.com"),
    ).toBeVisible();
    const settingsResponse = page.waitForResponse((response) => {
      const request = response.request();
      return (
        request.method() === "PATCH" &&
        new URL(response.url()).pathname ===
          `/api/v1/organizations/${organization.id}`
      );
    });
    await page.getByRole("button", { name: "Save", exact: true }).click();
    expect((await settingsResponse).status()).toBe(200);
    await expect(page.getByRole("status")).toHaveText(
      "Workspace settings saved.",
    );
    await expect(
      page.getByRole("button", {
        name: "Current workspace: E2E Membership Policy Renamed",
      }),
    ).toBeVisible();

    const deleteWorkspace = page.getByRole("button", {
      name: "Delete workspace",
    });
    await waitForInteraction(deleteWorkspace);
    await deleteWorkspace.click();
    await expect(
      page.getByRole("textbox", {
        name: 'Type "E2E Membership Policy Renamed" to confirm',
      }),
    ).toBeVisible();
    await page.getByRole("button", { name: "Cancel" }).click();
    await page.reload();
    await waitForInteraction(workspaceName);
    await expect(domains).toHaveValue("example.com\nsub.example.com");

    for (const foreignKey of [organization.canonicalKey, organization.id]) {
      await outsiderPage.goto(`/w/${foreignKey}/dashboard`);
      await expect(
        outsiderPage.getByRole("heading", { name: "403", exact: true }),
      ).toBeVisible();
      await expect(
        outsiderPage.getByRole("heading", {
          name: "This page could not be accessed.",
        }),
      ).toBeVisible();
      await expect(
        outsiderPage.getByRole("heading", { name: "Workspace dashboard" }),
      ).toHaveCount(0);
    }

    await page.goto(`/w/${organization.canonicalKey}/settings/users`);
    const addMember = page.getByRole("button", { name: "Add member" });
    await waitForOrganizationControlInteraction(addMember);
    await addMember.click();
    await page.getByLabel("User ID").fill(member.user.id);
    await page.getByRole("button", { name: "Add", exact: true }).click();
    await expect(
      page.getByText("Email domain outside policy", { exact: true }),
    ).toBeVisible();
    await expect(page.getByText(member.email, { exact: false })).toBeVisible();
    await expect(page.getByText("example.com", { exact: false })).toBeVisible();
    await expect(
      page.getByText("sub.example.com", { exact: false }),
    ).toBeVisible();
    await page.getByRole("button", { name: "Confirm add" }).click();

    const memberArticle = page.getByRole("article", {
      name: "E2E Organization Member workspace member",
    });
    await expect(memberArticle).toContainText("Outside domain policy");
    const roleControl = memberArticle.getByRole("combobox", {
      name: "Role for E2E Organization Member",
    });
    await waitForInteraction(roleControl);
    await roleControl.click();
    await page.getByRole("option", { name: "Administrator" }).click();
    await expect(page.getByRole("status")).toHaveText("Member role updated.");
    await expect(roleControl).toContainText("Administrator");
    await expect(
      page.getByRole("button", { name: /remove member|delete member/i }),
    ).toHaveCount(0);

    // An administrator intentionally has mutation capabilities. Return the
    // target to the member role before proving the read-only member context.
    await roleControl.click();
    await page.getByRole("option", { name: "Member", exact: true }).click();
    await expect(roleControl).toContainText("Member");

    await memberPage.goto(`/w/${organization.canonicalKey}/settings/workspace`);
    await expect(
      memberPage.getByText("Read-only workspace settings", { exact: true }),
    ).toBeVisible();
    await expect(memberPage.getByLabel("Workspace Name")).toBeDisabled();
    await expect(
      memberPage.getByRole("button", { name: "Save", exact: true }),
    ).toHaveCount(0);
    await expect(
      memberPage.getByRole("button", { name: "Delete workspace" }),
    ).toHaveCount(0);

    await memberPage.goto(`/w/${organization.canonicalKey}/settings/users`);
    const memberSettingsMain = memberPage.getByRole("main");
    await expect(
      memberSettingsMain.getByText(
        "Only workspace administrators and owners can add people or change roles.",
        { exact: true },
      ),
    ).toBeVisible();
    await expect(
      memberPage.getByRole("button", { name: "Add member" }),
    ).toHaveCount(0);
    await expect(memberPage.getByRole("combobox")).toHaveCount(0);
    await expect(
      memberPage.getByRole("button", {
        name: /remove member|delete member/i,
      }),
    ).toHaveCount(0);
  });

  test("two stale administrators patch only dirty settings fields", async ({
    organizationScenario,
    page,
  }) => {
    const adminContext = await organizationScenario.createContext(
      "stale settings admin context",
    );
    const adminPage = await adminContext.newPage();
    const [owner, admin] = await organizationScenario.createLocalUsers([
      organizationScenario.prepareLocalUser(
        page.context(),
        staleSettingsOwner,
        "stale settings owner",
      ),
      organizationScenario.prepareLocalUser(
        adminContext,
        staleSettingsAdmin,
        "stale settings admin",
      ),
    ]);
    const organization = await organizationScenario.createOrganization(
      owner,
      page.context().request,
      "E2E Stale Settings",
    );

    await page.goto(`/w/${organization.canonicalKey}/settings/users`);
    const addAdmin = page.getByRole("button", { name: "Add member" });
    await waitForOrganizationControlInteraction(addAdmin);
    await addAdmin.click();
    await page.getByLabel("User ID").fill(admin.user.id);
    await page.getByRole("combobox", { name: "Role" }).click();
    await page.getByRole("option", { name: "Administrator" }).click();
    await page.getByRole("button", { name: "Add", exact: true }).click();
    await expect(page.getByRole("status")).toHaveText("Member added.");

    await Promise.all([
      page.goto(`/w/${organization.canonicalKey}/settings/workspace`),
      adminPage.goto(`/w/${organization.canonicalKey}/settings/workspace`),
    ]);
    const ownerName = page.getByRole("textbox", { name: "Workspace Name" });
    const adminDomains = adminPage.getByRole("textbox", {
      name: "Allowed Email Domains",
    });
    await Promise.all([
      waitForInteraction(ownerName),
      waitForInteraction(adminDomains),
    ]);

    await ownerName.fill("E2E Owner Rename");
    const ownerPatch = page.waitForResponse((response) => {
      const request = response.request();
      return (
        request.method() === "PATCH" &&
        new URL(response.url()).pathname ===
          `/api/v1/organizations/${organization.id}`
      );
    });
    await page.getByRole("button", { name: "Save", exact: true }).click();
    const ownerResponse = await ownerPatch;
    expect(ownerResponse.status()).toBe(200);
    expect(ownerResponse.request().postDataJSON()).toEqual({
      name: "E2E Owner Rename",
    });

    await adminDomains.fill("Example.COM,\n@example.com");
    const adminPatch = adminPage.waitForResponse((response) => {
      const request = response.request();
      return (
        request.method() === "PATCH" &&
        new URL(response.url()).pathname ===
          `/api/v1/organizations/${organization.id}`
      );
    });
    await adminPage.getByRole("button", { name: "Save", exact: true }).click();
    const adminResponse = await adminPatch;
    expect(adminResponse.status()).toBe(200);
    expect(adminResponse.request().postDataJSON()).toEqual({
      allowedEmailDomains: ["example.com"],
    });
    await expect(
      adminPage.getByRole("textbox", { name: "Workspace Name" }),
    ).toHaveValue("E2E Owner Rename");

    await page.reload();
    await waitForInteraction(ownerName);
    await expect(ownerName).toHaveValue("E2E Owner Rename");
    await expect(
      page.getByRole("textbox", { name: "Allowed Email Domains" }),
    ).toHaveValue("example.com");
  });

  test("workspace load-more stays canonical and refreshes back to the authoritative first page", async ({
    organizationScenario,
    page,
  }) => {
    test.setTimeout(120_000);
    const owner = await organizationScenario.createLocalUser(
      page.context(),
      paginationOwner,
      "pagination owner",
    );
    for (let index = 1; index <= 51; index += 1) {
      await organizationScenario.createOrganization(
        owner,
        page.context().request,
        `E2E Pagination ${String(index).padStart(2, "0")}`,
      );
    }

    const fakeSummary = (id: string, name: string, canonicalKey: string) => ({
      id,
      name,
      slug: canonicalKey,
      canonicalKey,
      createdAt: "2026-07-31T10:00:00Z",
      updatedAt: "2026-07-31T10:00:00Z",
      accessPrincipal: "user",
      currentRole: "owner",
      capabilities: {
        canUpdateOrganization: true,
        canDeleteOrganization: true,
        canAddMembers: true,
        canUpdateMemberRoles: true,
        canManageTeams: true,
        canManageInvitations: true,
        canManageApiKeys: true,
      },
    });
    await page.route("**/api/v1/organizations?cursor=*", async (route) => {
      const cursor = new URL(route.request().url()).searchParams.get("cursor");
      const thirdPage = cursor === "browser-page-three";
      await route.fulfill({
        contentType: "application/json",
        status: 200,
        body: JSON.stringify({
          data: {
            items: [
              thirdPage
                ? fakeSummary(
                    "01900000-0000-7000-8000-000000000093",
                    "Browser Accumulated Page Three",
                    "browser-accumulated-page-three",
                  )
                : fakeSummary(
                    "01900000-0000-7000-8000-000000000092",
                    "Browser Accumulated Page Two",
                    "browser-accumulated-page-two",
                  ),
            ],
            nextCursor: thirdPage ? null : "browser-page-three",
          },
        }),
      });
    });

    await page.goto("/workspaces");
    const loadMore = page.getByRole("button", {
      name: "Load more workspaces",
    });
    await waitForOrganizationControlInteraction(loadMore);
    await loadMore.click();
    await expect(
      page.getByRole("article", {
        name: "Browser Accumulated Page Two workspace",
      }),
    ).toBeVisible();
    await page.getByRole("button", { name: "Load more workspaces" }).click();
    await expect(
      page.getByRole("article", {
        name: "Browser Accumulated Page Three workspace",
      }),
    ).toBeVisible();
    await expect(
      page.getByRole("article", {
        name: "Browser Accumulated Page Two workspace",
      }),
    ).toBeVisible();
    await expect(page).toHaveURL("/workspaces");

    await page.reload();
    await expect(
      page.getByRole("article", {
        name: "Browser Accumulated Page Two workspace",
      }),
    ).toHaveCount(0);
    await expect(
      page.getByRole("article", {
        name: "Browser Accumulated Page Three workspace",
      }),
    ).toHaveCount(0);

    const bookmarkedPage = await page.context().newPage();
    try {
      await bookmarkedPage.goto("/workspaces?cursor=browser-page-three");
      await expect(bookmarkedPage).toHaveURL("/workspaces");
      await expect(
        bookmarkedPage.getByRole("article", {
          name: "Browser Accumulated Page Two workspace",
        }),
      ).toHaveCount(0);
    } finally {
      await bookmarkedPage.close();
    }
  });

  test("slug collision, suffix-preserving switch, and last workspace guard", async ({
    organizationScenario,
    page,
  }) => {
    const owner = await organizationScenario.createLocalUser(
      page.context(),
      slugOwner,
      "slug owner",
    );
    const first = await organizationScenario.createOrganization(
      owner,
      page.context().request,
      "E2E Slug",
    );
    await page.goto("/workspaces");
    const second = await createWorkspaceThroughBrowser(
      page,
      organizationScenario,
      owner,
      "Create New Workspace",
      "E2E-Slug",
    );
    expect(first.canonicalKey).toBe("e2e-slug");
    expect(second.canonicalKey).toBe("e2e-slug-2");

    await page.goto("/w/e2e-slug/settings/users");
    const workspaceSwitcher = page.getByRole("button", {
      name: "Current workspace: E2E Slug",
    });
    await waitForOrganizationControlInteraction(workspaceSwitcher);
    await workspaceSwitcher.click();
    await page.getByRole("link", { name: "Manage workspaces" }).click();
    await expect(page).toHaveURL("/workspaces");
    await page
      .getByRole("article", { name: "E2E Slug workspace" })
      .getByRole("link", { name: "Open workspace" })
      .click();
    await expect(page).toHaveURL("/w/e2e-slug/dashboard");
    await expect(
      page.getByRole("heading", { name: "Switch workspace" }),
    ).toHaveCount(0);

    // Deep-linking to the first workspace does not alter the active preference.
    // Selecting that already-routed workspace explicitly must persist it.
    const currentRouteSwitcher = page.getByRole("button", {
      name: "Current workspace: E2E Slug",
    });
    await waitForOrganizationControlInteraction(currentRouteSwitcher);
    await currentRouteSwitcher.click();
    const persistCurrentResponse = page.waitForResponse((response) => {
      const request = response.request();
      return (
        request.method() === "PUT" &&
        new URL(response.url()).pathname ===
          "/api/v1/auth/session/active-organization"
      );
    });
    await page.getByRole("button", { name: "Switch to E2E Slug" }).click();
    expect((await persistCurrentResponse).status()).toBe(200);
    await expect(page).toHaveURL("/w/e2e-slug/dashboard");
    await page.goto("/dashboard");
    await expect(page).toHaveURL("/w/e2e-slug/dashboard");

    await page.goto("/w/e2e-slug/settings/users");
    const reopenedSwitcher = page.getByRole("button", {
      name: "Current workspace: E2E Slug",
    });
    await waitForOrganizationControlInteraction(reopenedSwitcher);
    await reopenedSwitcher.click();
    await page.getByRole("button", { name: "Switch to E2E-Slug" }).click();
    await expect(page).toHaveURL("/w/e2e-slug-2/settings/users");

    await page.goto(`/w/${second.id}`);
    await expect(page).toHaveURL("/w/e2e-slug-2/dashboard");

    expect(
      await organizationScenario.deleteOrganization(
        owner,
        page.context().request,
        first,
      ),
    ).toBe(first.id);
    await page.goto("/w/e2e-slug-2/settings/workspace");
    const slug = page.getByRole("textbox", { name: "Workspace Slug" });
    await waitForInteraction(slug);
    await slug.fill("e2e-slug-final");
    const slugUpdateResponse = page.waitForResponse((response) => {
      const request = response.request();
      return (
        request.method() === "PATCH" &&
        new URL(response.url()).pathname ===
          `/api/v1/organizations/${second.id}`
      );
    });
    await page.getByRole("button", { name: "Save", exact: true }).click();
    expect((await slugUpdateResponse).status()).toBe(200);
    await expect(page).toHaveURL("/w/e2e-slug-final/settings/workspace");
    await expect(
      page.getByRole("heading", { name: "Workspace settings" }),
    ).toBeVisible();
    await expect(
      page.getByRole("button", { name: "Delete workspace" }),
    ).toHaveCount(0);
  });
});
