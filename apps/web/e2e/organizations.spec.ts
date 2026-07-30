import {
  expect,
  test,
  type APIRequestContext,
  type BrowserContext,
} from "@playwright/test";

import type { LocalAutomationScenarioResponse } from "@/src/lib/api/generated";
import {
  cleanupLocalAutomationUser,
  createLocalAutomationUser,
} from "./support/generated-auth-api";
import {
  createGeneratedOrganization,
  deleteGeneratedOrganization,
  getGeneratedOrganizations,
  setGeneratedOrganizationAllowedEmailDomains,
} from "./support/generated-organizations-api";

const localPassword = "E2E-Organization-Owner-123!";

function deletedOrganizationCount(value: number | string): number {
  return Number(value);
}

async function createIsolatedLocalUser(
  context: BrowserContext,
  identity: Readonly<{ email: string; name: string }>,
): Promise<LocalAutomationScenarioResponse> {
  return createLocalAutomationUser(context.request, {
    ...identity,
    password: localPassword,
  });
}

async function expectCleanupCount(
  request: APIRequestContext,
  expected: number,
) {
  const cleanup = await cleanupLocalAutomationUser(request);
  expect(deletedOrganizationCount(cleanup.deletedOrganizations)).toBe(expected);
}

async function expectNoFutureOrganizationLinks(context: BrowserContext) {
  const page = context.pages()[0];
  for (const futureSurface of [/invitations?/i, /teams?/i, /api keys?/i]) {
    await expect(page.getByRole("link", { name: futureSurface })).toHaveCount(
      0,
    );
  }
}

test.describe.serial("organization full-stack workflows", () => {
  test("zero organization onboarding and first workspace", async ({ page }) => {
    let localUserCreated = false;
    let expectedCleanupOrganizations = 0;

    try {
      await createIsolatedLocalUser(page.context(), {
        name: "E2E Organization Owner",
        email: "local-agent+organization-onboarding-owner@local-agent.test",
      });
      localUserCreated = true;
      await page.goto("/dashboard");
      await expect(page).toHaveURL("/welcome");
      await expect(
        page.getByRole("heading", { name: "Create your first workspace" }),
      ).toBeVisible();
      await page.getByRole("button", { name: "Create Workspace" }).click();
      await page.getByLabel("Workspace Name").fill("E2E Organization");
      await page.getByRole("button", { name: "Create", exact: true }).click();
      await expect(page).toHaveURL("/w/e2e-organization/dashboard");
      expectedCleanupOrganizations = 1;

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

      await page.goto("/w/e2e-organization/settings/workspace");
      await expect(
        page.getByRole("navigation", { name: "Workspace settings" }),
      ).toBeVisible();
      await expectNoFutureOrganizationLinks(page.context());
    } finally {
      if (localUserCreated) {
        await expectCleanupCount(
          page.context().request,
          expectedCleanupOrganizations,
        );
      }
    }
  });

  test("owner adds an outside-domain member and member settings stay read-only", async ({
    browser,
    page,
  }) => {
    let expectedOwnerCleanupOrganizations = 0;
    const memberContext = await browser.newContext();
    const memberPage = await memberContext.newPage();
    let owner: LocalAutomationScenarioResponse | undefined;
    let member: LocalAutomationScenarioResponse | undefined;

    try {
      owner = await createIsolatedLocalUser(page.context(), {
        name: "E2E Membership Owner",
        email: "local-agent+organization-membership-owner@local-agent.test",
      });
      member = await createIsolatedLocalUser(memberContext, {
        name: "E2E Organization Member",
        email: "local-agent+organization-membership-member@local-agent.test",
      });
      expect(owner.user.id).not.toBe(member.user.id);
      const organization = await createGeneratedOrganization(
        page.context().request,
        "E2E Membership Policy",
      );
      expectedOwnerCleanupOrganizations = 1;
      await setGeneratedOrganizationAllowedEmailDomains(
        page.context().request,
        organization.id,
        ["example.com"],
      );

      await page.goto(`/w/${organization.canonicalKey}/settings/users`);
      await page.getByRole("button", { name: "Add member" }).click();
      await page.getByLabel("User ID").fill(member.user.id);
      await page.getByRole("button", { name: "Add", exact: true }).click();
      await expect(
        page.getByText("Email domain outside policy", { exact: true }),
      ).toBeVisible();
      await expect(
        page.getByText(member.email, { exact: false }),
      ).toBeVisible();
      await expect(
        page.getByText("example.com", { exact: false }),
      ).toBeVisible();
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

      await memberPage.goto(
        `/w/${organization.canonicalKey}/settings/workspace`,
      );
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
      await expect(
        memberPage.getByText(
          "Only workspace administrators and owners can add people or change roles.",
          { exact: true },
        ),
      ).toBeVisible();
      await expect(
        memberPage.getByRole("button", { name: "Add member" }),
      ).toHaveCount(0);
      await expect(
        memberPage.getByRole("combobox", {
          name: "Role for E2E Membership Owner",
        }),
      ).toHaveCount(0);
      await expect(
        memberPage.getByRole("button", {
          name: /remove member|delete member/i,
        }),
      ).toHaveCount(0);
    } finally {
      try {
        if (member) {
          await expectCleanupCount(memberContext.request, 0);
        }
      } finally {
        try {
          await memberContext.close();
        } finally {
          if (owner) {
            await expectCleanupCount(
              page.context().request,
              expectedOwnerCleanupOrganizations,
            );
          }
        }
      }
    }
  });

  test("slug collision, suffix-preserving switch, and last workspace guard", async ({
    page,
  }) => {
    let localUserCreated = false;
    let expectedCleanupOrganizations = 0;

    try {
      await createIsolatedLocalUser(page.context(), {
        name: "E2E Slug Owner",
        email: "local-agent+organization-slug-owner@local-agent.test",
      });
      localUserCreated = true;
      const first = await createGeneratedOrganization(
        page.context().request,
        "E2E Slug",
      );
      expectedCleanupOrganizations += 1;
      const second = await createGeneratedOrganization(
        page.context().request,
        "E2E-Slug",
      );
      expectedCleanupOrganizations += 1;
      expect(first.canonicalKey).toBe("e2e-slug");
      expect(second.canonicalKey).toBe("e2e-slug-2");

      await page.goto("/w/e2e-slug/settings/users");
      await page
        .getByRole("button", { name: "Current workspace: E2E Slug" })
        .click();
      await page.getByRole("button", { name: "Switch to E2E-Slug" }).click();
      await expect(page).toHaveURL("/w/e2e-slug-2/settings/users");

      await page.goto(`/w/${second.id}`);
      await expect(page).toHaveURL("/w/e2e-slug-2/dashboard");

      expect(
        await deleteGeneratedOrganization(page.context().request, first),
      ).toBe(first.id);
      expectedCleanupOrganizations -= 1;
      await page.goto("/w/e2e-slug-2/settings/workspace");
      await expect(
        page.getByRole("heading", { name: "Workspace settings" }),
      ).toBeVisible();
      await expect(
        page.getByRole("button", { name: "Delete workspace" }),
      ).toHaveCount(0);
    } finally {
      if (localUserCreated) {
        await expectCleanupCount(
          page.context().request,
          expectedCleanupOrganizations,
        );
      }
    }
  });
});
