import type { Page } from "@playwright/test";

import { waitForAppHydration } from "./support/app-readiness";
import {
  getGeneratedOrganizations,
  setGeneratedOrganizationAllowedEmailDomains,
} from "./support/generated-organizations-api";
import {
  expect,
  test,
  type OrganizationTestIdentity,
} from "./support/organization-test-fixture";

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

const slugOwner: OrganizationTestIdentity = {
  name: "E2E Slug Owner",
  email: "local-agent+organization-slug-owner@local-agent.test",
  password: localPassword,
};

async function expectNoFutureOrganizationLinks(page: Page) {
  for (const futureSurface of [/invitations?/i, /teams?/i, /api keys?/i]) {
    await expect(page.getByRole("link", { name: futureSurface })).toHaveCount(
      0,
    );
  }
}

function isOrganizationCreateResponse(url: string, method: string) {
  return method === "POST" && new URL(url).pathname === "/api/v1/organizations";
}

test.describe.serial("organization full-stack workflows", () => {
  test("zero organization onboarding and first workspace", async ({
    organizationScenario,
    page,
  }) => {
    await organizationScenario.preflightLocalUsers([onboardingOwner]);
    const owner = await organizationScenario.createLocalUser(
      page.context(),
      onboardingOwner,
      "onboarding owner",
    );

    await page.goto("/dashboard");
    await waitForAppHydration(page);
    await expect(page).toHaveURL("/welcome");
    await expect(
      page.getByRole("heading", { name: "Create your first workspace" }),
    ).toBeVisible();
    await page.getByRole("button", { name: "Create Workspace" }).click();
    await page.getByLabel("Workspace Name").fill("E2E Organization");

    const persistedOrganization = page.waitForResponse((response) => {
      const request = response.request();
      return isOrganizationCreateResponse(response.url(), request.method());
    });
    await page.getByRole("button", { name: "Create", exact: true }).click();
    const createResponse = await persistedOrganization;
    expect(createResponse.status()).toBe(201);
    organizationScenario.organizationCreated(owner);

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
    await waitForAppHydration(page);
    await expect(page).toHaveURL("/w/e2e-organization/dashboard");

    await page.goto("/w/e2e-organization/settings/workspace");
    await waitForAppHydration(page);
    await expect(
      page.getByRole("navigation", { name: "Workspace settings" }),
    ).toBeVisible();
    await expectNoFutureOrganizationLinks(page);
  });

  test("owner adds an outside-domain member and member settings stay read-only", async ({
    organizationScenario,
    page,
  }) => {
    await organizationScenario.preflightLocalUsers([
      membershipOwner,
      membershipMember,
    ]);
    const owner = await organizationScenario.createLocalUser(
      page.context(),
      membershipOwner,
      "membership owner",
    );
    const memberContext = await organizationScenario.createContext(
      "membership member context",
    );
    const memberPage = await memberContext.newPage();
    const member = await organizationScenario.createLocalUser(
      memberContext,
      membershipMember,
      "membership member",
    );
    expect(owner.user.id).not.toBe(member.user.id);

    const organization = await organizationScenario.createOrganization(
      owner,
      page.context().request,
      "E2E Membership Policy",
    );
    await setGeneratedOrganizationAllowedEmailDomains(
      page.context().request,
      organization.id,
      ["example.com"],
    );

    await page.goto(`/w/${organization.canonicalKey}/settings/users`);
    await waitForAppHydration(page);
    await page.getByRole("button", { name: "Add member" }).click();
    await page.getByLabel("User ID").fill(member.user.id);
    await page.getByRole("button", { name: "Add", exact: true }).click();
    await expect(
      page.getByText("Email domain outside policy", { exact: true }),
    ).toBeVisible();
    await expect(page.getByText(member.email, { exact: false })).toBeVisible();
    await expect(page.getByText("example.com", { exact: false })).toBeVisible();
    await page.getByRole("button", { name: "Confirm add" }).click();

    const memberArticle = page.getByRole("article", {
      name: "E2E Organization Member workspace member",
    });
    await expect(memberArticle).toContainText("Outside domain policy");
    const roleControl = memberArticle.getByRole("combobox", {
      name: "Role for E2E Organization Member",
    });
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
    await waitForAppHydration(memberPage);
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
    await waitForAppHydration(memberPage);
    await expect(
      memberPage.getByText(
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

  test("slug collision, suffix-preserving switch, and last workspace guard", async ({
    organizationScenario,
    page,
  }) => {
    await organizationScenario.preflightLocalUsers([slugOwner]);
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
    const second = await organizationScenario.createOrganization(
      owner,
      page.context().request,
      "E2E-Slug",
    );
    expect(first.canonicalKey).toBe("e2e-slug");
    expect(second.canonicalKey).toBe("e2e-slug-2");

    await page.goto("/w/e2e-slug/settings/users");
    await waitForAppHydration(page);
    await page
      .getByRole("button", { name: "Current workspace: E2E Slug" })
      .click();
    await page.getByRole("button", { name: "Switch to E2E-Slug" }).click();
    await expect(page).toHaveURL("/w/e2e-slug-2/settings/users");

    await page.goto(`/w/${second.id}`);
    await waitForAppHydration(page);
    await expect(page).toHaveURL("/w/e2e-slug-2/dashboard");

    expect(
      await organizationScenario.deleteOrganization(
        owner,
        page.context().request,
        first,
      ),
    ).toBe(first.id);
    await page.goto("/w/e2e-slug-2/settings/workspace");
    await waitForAppHydration(page);
    await expect(
      page.getByRole("heading", { name: "Workspace settings" }),
    ).toBeVisible();
    await expect(
      page.getByRole("button", { name: "Delete workspace" }),
    ).toHaveCount(0);
  });
});
