import type { BrowserContext, Page } from "@playwright/test";

import {
  addGeneratedOrganizationMember,
  createGeneratedTeam,
  getGeneratedAccountInvitations,
  getGeneratedOrganizationInvitations,
  getGeneratedOrganizationMembers,
  getGeneratedTeamMemberCandidates,
  getGeneratedTeamMembers,
  getGeneratedTeams,
} from "./support/generated-collaboration-api";
import { getGeneratedAuthSession } from "./support/generated-auth-api";
import { setGeneratedOrganizationAllowedEmailDomains } from "./support/generated-organizations-api";
import {
  expect,
  test,
  type OrganizationTestIdentity,
} from "./support/organization-test-fixture";

const password = "E2E-Collaboration-123!";
const webOrigin = "http://127.0.0.1:3127";
const identities = {
  teamOwner: {
    name: "E2E Team Owner",
    email: "local-agent+collaboration-team-owner@local-agent.test",
    password,
  },
  teamAdmin: {
    name: "E2E Team Administrator",
    email: "local-agent+collaboration-team-admin@local-agent.test",
    password,
  },
  teamMember: {
    name: "E2E Team Member",
    email: "local-agent+collaboration-team-member@local-agent.test",
    password,
  },
  invitationOwner: {
    name: "E2E Invitation Owner",
    email: "local-agent+collaboration-invitation-owner@local-agent.test",
    password,
  },
  invitationAdmin: {
    name: "E2E Invitation Administrator",
    email: "local-agent+collaboration-invitation-admin@local-agent.test",
    password,
  },
  invitationMember: {
    name: "E2E Invitation Member",
    email: "local-agent+collaboration-invitation-member@local-agent.test",
    password,
  },
  acceptingInvitee: {
    name: "E2E Accepting Invitee",
    email: "local-agent+collaboration-accepting-invitee@local-agent.test",
    password,
  },
  rejectingInvitee: {
    name: "E2E Rejecting Invitee",
    email: "local-agent+collaboration-rejecting-invitee@local-agent.test",
    password,
  },
  invitedOwner: {
    name: "E2E Invited Owner",
    email: "local-agent+collaboration-invited-owner@local-agent.test",
    password,
  },
  mismatchUser: {
    name: "E2E Invitation Outsider",
    email: "local-agent+collaboration-invitation-outsider@local-agent.test",
    password,
  },
} satisfies Record<string, OrganizationTestIdentity>;

async function createTrackedContext(
  organizationScenario: Readonly<{
    createContext(label: string): Promise<BrowserContext>;
  }>,
  label: string,
) {
  const context = await organizationScenario.createContext(label);
  return { context, page: await context.newPage() };
}

async function expectForbidden(page: Page) {
  await expect(
    page.getByRole("heading", { name: "Access denied", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText("You do not have permission to open this page.", {
      exact: true,
    }),
  ).toBeVisible();
}

async function createInvitationThroughBrowser(
  page: Page,
  options: Readonly<{
    email: string;
    roleName?: "Member" | "Administrator" | "Owner";
    teamName?: string;
  }>,
): Promise<Readonly<{ absoluteUrl: string; id: string; path: string }>> {
  const open = page.getByRole("button", { name: "Create invitation" });
  await expect(open).toBeEnabled();
  await open.click();
  await page.getByLabel("Email address").fill(options.email);
  const role = page.getByRole("combobox", { name: "Workspace role" });
  await expect(role).toContainText("Member");
  if (options.roleName) {
    await role.click();
    const roleOption = page.getByRole("option", {
      name: options.roleName,
      exact: true,
    });
    await expect(roleOption).toBeVisible();
    await roleOption.click();
    await expect(role).toContainText(options.roleName);
  }
  if (options.teamName) {
    await page.getByLabel("Team", { exact: true }).click();
    await page.getByRole("option", { name: options.teamName }).click();
  }
  await page
    .getByRole("button", { name: "Create invitation", exact: true })
    .click();
  await expect(
    page.getByText("Invitation created.", { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText(
      "No invitation email is sent in this iteration. Copy and share the link manually.",
      { exact: true },
    ),
  ).toBeVisible();
  const invitationLink = page.getByLabel("Invitation link");
  const absolute = await invitationLink.inputValue();
  const url = new URL(absolute);
  expect(url.origin).toBe(webOrigin);
  expect(url.pathname).toMatch(/^\/invite\/[0-9a-f-]{36}$/u);
  await page.getByRole("button", { name: "Copy invitation link" }).click();
  await expect(
    page.getByText("Invitation link copied.", { exact: true }),
  ).toBeVisible();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(
    absolute,
  );
  await page.getByRole("button", { name: "Close", exact: true }).click();
  return {
    absoluteUrl: absolute,
    id: url.pathname.slice("/invite/".length),
    path: url.pathname,
  };
}

async function confirmLocalRecipient(page: Page, invitationPath: string) {
  await page.goto(invitationPath);
  const main = page.getByRole("main");
  await expect(
    main.getByText("Verify the invited email address before responding.", {
      exact: true,
    }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Accept invitation" }),
  ).toHaveCount(0);
  const confirmation = page.waitForResponse(
    (response) =>
      response.request().method() === "POST" &&
      new URL(response.url()).pathname === "/api/local-auth/confirm-email",
  );
  await page
    .getByRole("button", { name: "Confirm email for local testing" })
    .click();
  const confirmationResponse = await confirmation;
  expect(confirmationResponse.status()).toBe(200);
  expect(await confirmationResponse.headerValue("set-cookie")).toContain(
    "__Host-template.session=",
  );
  await expect(
    page.getByRole("button", { name: "Accept invitation" }),
  ).toBeVisible();
  expect(
    (await getGeneratedAuthSession(page.context().request)).user?.emailVerified,
  ).toBe(true);
}

test.describe("collaboration full-stack workflows", () => {
  test.describe.configure({ timeout: 90_000 });
  test("owner manages team membership while an ordinary member sees read-only composition", async ({
    organizationScenario,
    page,
  }) => {
    const admin = await createTrackedContext(
      organizationScenario,
      "team administrator context",
    );
    const member = await createTrackedContext(
      organizationScenario,
      "team member context",
    );
    const [ownerUser, adminUser, memberUser] =
      await organizationScenario.createLocalUsers([
        organizationScenario.prepareLocalUser(
          page.context(),
          identities.teamOwner,
          "team owner",
        ),
        organizationScenario.prepareLocalUser(
          admin.context,
          identities.teamAdmin,
          "team administrator",
        ),
        organizationScenario.prepareLocalUser(
          member.context,
          identities.teamMember,
          "team member",
        ),
      ]);
    const organization = await organizationScenario.createOrganization(
      ownerUser,
      page.context().request,
      "E2E Collaboration Teams",
    );
    await addGeneratedOrganizationMember(
      page.context().request,
      organization.id,
      adminUser.user.id,
      "admin",
    );
    await addGeneratedOrganizationMember(
      page.context().request,
      organization.id,
      memberUser.user.id,
      "member",
    );

    await page.goto(`/w/${organization.canonicalKey}/settings/teams`);
    await expect(
      page.getByRole("heading", { name: "Workspace teams", level: 1 }),
    ).toBeVisible();
    const createTeamButton = page.getByRole("button", { name: "Create team" });
    await expect(createTeamButton).toBeEnabled();
    const createTeamForm = createTeamButton.locator("xpath=ancestor::form");
    await createTeamForm.getByLabel("Team name").fill("E2E Platform Team");
    await createTeamForm
      .getByRole("button", { name: "Create team", exact: true })
      .click();
    await expect(
      page.getByText("Team created.", { exact: true }),
    ).toBeVisible();

    const renameTeamButton = page.getByRole("button", {
      name: "Rename team",
      exact: true,
    });
    const renameTeamForm = renameTeamButton.locator("xpath=ancestor::form");
    await renameTeamForm.getByLabel("Team name").fill("E2E Core Team");
    await renameTeamForm
      .getByRole("button", { name: "Rename team", exact: true })
      .click();
    await expect(
      page.getByText("Team renamed.", { exact: true }),
    ).toBeVisible();

    const [team] = (
      await getGeneratedTeams(page.context().request, organization.id)
    ).items;
    expect(team).toMatchObject({ name: "E2E Core Team", memberCount: 0 });
    const candidates = await getGeneratedTeamMemberCandidates(
      page.context().request,
      organization.id,
      team.id,
      identities.teamMember.email,
    );
    expect(candidates.items.map((candidate) => candidate.userId)).toContain(
      memberUser.user.id,
    );

    await page
      .getByRole("button", { name: "Add member to E2E Core Team" })
      .click();
    await page
      .getByLabel("Find a workspace member")
      .fill(identities.teamMember.email);
    await page.getByRole("button", { name: "Search", exact: true }).click();
    await page.getByRole("button", { name: "Add E2E Team Member" }).click();
    await expect(
      page.getByText("Member added to the team.", { exact: true }),
    ).toBeVisible();
    await expect(
      page.getByRole("region", { name: "E2E Core Team members" }),
    ).toContainText(identities.teamMember.email);

    await member.page.goto(`/w/${organization.canonicalKey}/settings/teams`);
    await expect(
      member.page.getByText("Read-only team access", { exact: true }),
    ).toBeVisible();
    await expect(
      member.page.getByText("E2E Core Team", { exact: true }),
    ).toBeVisible();
    await expect(
      member.page.getByText(identities.teamMember.email, { exact: true }),
    ).toBeVisible();
    for (const name of [
      "Create team",
      "Rename team",
      "Delete E2E Core Team",
      "Add member to E2E Core Team",
      "Remove E2E Team Member",
    ]) {
      await expect(member.page.getByRole("button", { name })).toHaveCount(0);
    }

    await page.getByRole("button", { name: "Remove E2E Team Member" }).click();
    await expect(
      page.getByText("Member removed from the team.", { exact: true }),
    ).toBeVisible();
    const ownerTeamRegion = page.getByRole("region", {
      name: "E2E Core Team members",
    });
    await expect(
      ownerTeamRegion.getByText(identities.teamMember.email, { exact: true }),
    ).toHaveCount(0);
    await expect(
      ownerTeamRegion.getByRole("button", {
        name: "Remove E2E Team Member",
      }),
    ).toHaveCount(0);
    const membersAfterRemoval = await getGeneratedTeamMembers(
      page.context().request,
      organization.id,
      team.id,
    );
    expect(
      membersAfterRemoval.items.filter(
        (teamMember) => teamMember.userId === memberUser.user.id,
      ),
    ).toHaveLength(0);
    await page.getByRole("button", { name: "Delete E2E Core Team" }).click();
    await page
      .getByRole("button", { name: "Delete team", exact: true })
      .click();
    await expect(
      page.getByText("Team deleted.", { exact: true }),
    ).toBeVisible();
    await expect(page.getByText("No teams yet", { exact: true })).toBeVisible();
  });

  test("owner, admin, member, invitees, and outsider observe safe invitation decisions", async ({
    organizationScenario,
    page,
  }) => {
    const admin = await createTrackedContext(
      organizationScenario,
      "invitation admin context",
    );
    const member = await createTrackedContext(
      organizationScenario,
      "invitation member context",
    );
    const accepting = await createTrackedContext(
      organizationScenario,
      "accepting invitee context",
    );
    const rejecting = await createTrackedContext(
      organizationScenario,
      "rejecting invitee context",
    );
    const invitedOwner = await createTrackedContext(
      organizationScenario,
      "invited owner context",
    );
    const outsider = await createTrackedContext(
      organizationScenario,
      "mismatch outsider context",
    );
    await admin.context.grantPermissions(
      ["clipboard-read", "clipboard-write"],
      { origin: webOrigin },
    );
    await page
      .context()
      .grantPermissions(["clipboard-read", "clipboard-write"], {
        origin: webOrigin,
      });

    const [
      ownerUser,
      adminUser,
      memberUser,
      acceptingUser,
      rejectingUser,
      invitedOwnerUser,
    ] = await organizationScenario.createLocalUsers([
      organizationScenario.prepareLocalUser(
        page.context(),
        identities.invitationOwner,
        "invitation owner",
      ),
      organizationScenario.prepareLocalUser(
        admin.context,
        identities.invitationAdmin,
        "invitation admin",
      ),
      organizationScenario.prepareLocalUser(
        member.context,
        identities.invitationMember,
        "invitation member",
      ),
      organizationScenario.prepareLocalUser(
        accepting.context,
        identities.acceptingInvitee,
        "accepting invitee",
      ),
      organizationScenario.prepareLocalUser(
        rejecting.context,
        identities.rejectingInvitee,
        "rejecting invitee",
      ),
      organizationScenario.prepareLocalUser(
        invitedOwner.context,
        identities.invitedOwner,
        "invited owner",
      ),
      organizationScenario.prepareLocalUser(
        outsider.context,
        identities.mismatchUser,
        "invitation outsider",
      ),
    ]);
    expect(acceptingUser.user.emailVerified).toBe(false);
    expect(rejectingUser.user.emailVerified).toBe(false);
    const organization = await organizationScenario.createOrganization(
      ownerUser,
      page.context().request,
      "E2E Invitation Workspace",
    );
    const [adminMembership] = await Promise.all([
      addGeneratedOrganizationMember(
        page.context().request,
        organization.id,
        adminUser.user.id,
        "admin",
      ),
      addGeneratedOrganizationMember(
        page.context().request,
        organization.id,
        memberUser.user.id,
        "member",
      ),
      setGeneratedOrganizationAllowedEmailDomains(
        page.context().request,
        organization.id,
        ["local-agent.test"],
      ),
    ]);
    const team = await createGeneratedTeam(
      page.context().request,
      organization.id,
      "E2E Invitation Team",
    );

    await page.goto(`/w/${organization.canonicalKey}/settings/invitations`);
    const acceptingInvitation = await createInvitationThroughBrowser(page, {
      email: identities.acceptingInvitee.email,
      teamName: team.name,
    });
    expect(acceptingInvitation.path).toBe(`/invite/${acceptingInvitation.id}`);

    await admin.page.goto(
      `/w/${organization.canonicalKey}/settings/invitations`,
    );
    await admin.page.getByRole("button", { name: "Create invitation" }).click();
    await admin.page
      .getByLabel("Email address")
      .fill(identities.acceptingInvitee.email);
    await admin.page
      .getByRole("button", { name: "Create invitation", exact: true })
      .click();
    await expect(
      admin.page.getByText(
        "A pending invitation already exists for this email address.",
        {
          exact: true,
        },
      ),
    ).toBeVisible();
    await admin.page
      .getByRole("button", { name: "Cancel", exact: true })
      .click();

    const forcedOwnerEmail =
      "local-agent+collaboration-admin-forced-owner@local-agent.test";
    await admin.page.reload();
    await admin.page.getByRole("button", { name: "Create invitation" }).click();
    const adminRole = admin.page.getByRole("combobox", {
      name: "Workspace role",
    });
    await expect(adminRole).toContainText("Member");
    await adminRole.click();
    await expect(
      admin.page.getByRole("option", { name: "Owner", exact: true }),
    ).toHaveCount(0);
    await admin.page.keyboard.press("Escape");
    await admin.page.getByLabel("Email address").fill(forcedOwnerEmail);
    const organizationInvitationsPath = `/api/v1/organizations/${organization.id}/invitations`;
    const organizationInvitationsPattern = `**${organizationInvitationsPath}`;
    await admin.page.route(organizationInvitationsPattern, async (route) => {
      if (route.request().method() !== "POST") {
        await route.continue();
        return;
      }
      const originalBody = route.request().postDataJSON() as Record<
        string,
        unknown
      >;
      await route.continue({
        postData: JSON.stringify({ ...originalBody, role: "owner" }),
      });
    });
    const forcedOwnerResponsePromise = admin.page.waitForResponse(
      (response) =>
        response.request().method() === "POST" &&
        new URL(response.url()).pathname === organizationInvitationsPath,
    );
    await admin.page
      .getByRole("button", { name: "Create invitation", exact: true })
      .click();
    const forcedOwnerResponse = await forcedOwnerResponsePromise;
    expect(forcedOwnerResponse.status()).toBe(403);
    expect(await forcedOwnerResponse.json()).toMatchObject({
      code: "invitation_permission_denied",
    });
    await expect(
      admin.page.getByText(
        "You do not have permission to manage workspace invitations.",
        { exact: true },
      ),
    ).toBeVisible();
    await admin.page.unroute(organizationInvitationsPattern);
    expect(
      (
        await getGeneratedOrganizationInvitations(
          page.context().request,
          organization.id,
        )
      ).items.filter((invitation) => invitation.email === forcedOwnerEmail),
    ).toHaveLength(0);
    await admin.page
      .getByRole("button", { name: "Cancel", exact: true })
      .click();

    await page.reload();
    await page.getByRole("button", { name: "Create invitation" }).click();
    await page.getByLabel("Email address").fill("outside@example.test");
    await page
      .getByRole("button", { name: "Create invitation", exact: true })
      .click();
    await expect(
      page.getByText(
        "The recipient email domain is not allowed by this workspace.",
        { exact: true },
      ),
    ).toBeVisible();
    await page.getByRole("button", { name: "Cancel", exact: true }).click();

    await member.page.goto(
      `/w/${organization.canonicalKey}/settings/invitations`,
    );
    await expectForbidden(member.page);
    await expect(
      member.page.getByText(identities.acceptingInvitee.email),
    ).toHaveCount(0);

    await page.reload();
    const ownerInvitation = await createInvitationThroughBrowser(page, {
      email: identities.invitedOwner.email,
      roleName: "Owner",
    });

    await admin.page.reload();
    const rejectingInvitation = await createInvitationThroughBrowser(
      admin.page,
      {
        email: identities.rejectingInvitee.email,
      },
    );
    expect(adminMembership.role).toBe("admin");

    await accepting.page.goto("/welcome");
    await expect(
      accepting.page.getByRole("heading", {
        name: "Create your first workspace",
      }),
    ).toBeVisible();
    await confirmLocalRecipient(accepting.page, acceptingInvitation.path);
    await expect(accepting.page.getByRole("main")).toHaveCount(1);
    await expect(accepting.page.getByRole("main")).toHaveAttribute(
      "id",
      "main-content",
    );
    await accepting.page.goto("/user/invitations");
    await expect(
      accepting.page.getByRole("heading", { name: "Invitations", exact: true }),
    ).toBeVisible();
    await expect(
      accepting.page.getByText("E2E Invitation Workspace", { exact: true }),
    ).toBeVisible();
    await accepting.page.goto("/welcome");
    await expect(
      accepting.page.getByText("E2E Invitation Workspace", { exact: true }),
    ).toBeVisible();

    const racePage = await accepting.context.newPage();
    await racePage.goto(acceptingInvitation.path);
    await accepting.page.goto(acceptingInvitation.path);
    let interceptedDecisions = 0;
    let releaseDecisions!: () => void;
    let bothDecisionsIntercepted!: () => void;
    const release = new Promise<void>((resolve) => {
      releaseDecisions = resolve;
    });
    const bothIntercepted = new Promise<void>((resolve) => {
      bothDecisionsIntercepted = resolve;
    });
    const acceptPattern = `**/api/v1/invitations/${acceptingInvitation.id}/accept`;
    await accepting.context.route(acceptPattern, async (route) => {
      interceptedDecisions += 1;
      if (interceptedDecisions === 2) bothDecisionsIntercepted();
      await release;
      await route.continue();
    });
    const acceptingDecisionResponse = accepting.page.waitForResponse(
      (response) =>
        response.request().method() === "POST" &&
        new URL(response.url()).pathname ===
          `/api/v1/invitations/${acceptingInvitation.id}/accept`,
    );
    const raceDecisionResponse = racePage.waitForResponse(
      (response) =>
        response.request().method() === "POST" &&
        new URL(response.url()).pathname ===
          `/api/v1/invitations/${acceptingInvitation.id}/accept`,
    );
    const clicks = Promise.all([
      accepting.page.getByRole("button", { name: "Accept invitation" }).click(),
      racePage.getByRole("button", { name: "Accept invitation" }).click(),
    ]);
    await bothIntercepted;
    releaseDecisions();
    await clicks;
    const decisionOutcomes = [
      {
        page: accepting.page,
        response: await acceptingDecisionResponse,
      },
      { page: racePage, response: await raceDecisionResponse },
    ];
    await accepting.context.unroute(acceptPattern);
    expect(
      decisionOutcomes.map(({ response }) => response.status()).sort(),
    ).toEqual([200, 409]);
    const winner = decisionOutcomes.find(
      ({ response }) => response.status() === 200,
    );
    const loser = decisionOutcomes.find(
      ({ response }) => response.status() === 409,
    );
    expect(winner).toBeDefined();
    expect(loser).toBeDefined();
    expect(await winner!.response.json()).toMatchObject({
      data: {
        invitationId: acceptingInvitation.id,
        organizationId: organization.id,
        canonicalOrganizationKey: organization.canonicalKey,
      },
    });
    expect(await loser!.response.json()).toMatchObject({
      code: "invitation_not_pending",
    });

    const canonicalDashboard = `/w/${encodeURIComponent(
      organization.canonicalKey,
    )}/dashboard`;
    await expect(winner!.page).toHaveURL(`${webOrigin}${canonicalDashboard}`);
    await expect(
      loser!.page.getByText("This invitation has been accepted.", {
        exact: true,
      }),
    ).toBeVisible();
    await expect(loser!.page).toHaveURL(
      `${webOrigin}${acceptingInvitation.path}`,
    );
    await expect(
      loser!.page.getByRole("button", { name: "Accept invitation" }),
    ).toHaveCount(0);
    await expect(
      loser!.page.getByRole("button", { name: "Reject invitation" }),
    ).toHaveCount(0);
    await expect(
      winner!.page.getByRole("heading", { name: "Workspace dashboard" }),
    ).toBeVisible();
    await winner!.page.goto(
      `/w/${encodeURIComponent(organization.canonicalKey)}/settings/teams`,
    );
    const acceptedTeamDirectory = winner!.page.getByRole("region", {
      name: "Workspace teams",
    });
    await expect(
      acceptedTeamDirectory.getByText("E2E Invitation Team", { exact: true }),
    ).toBeVisible();
    await expect(
      acceptedTeamDirectory.getByText(identities.acceptingInvitee.email, {
        exact: true,
      }),
    ).toBeVisible();
    const acceptedOrganizationMembers = await getGeneratedOrganizationMembers(
      winner!.page.context().request,
      organization.id,
    );
    expect(
      acceptedOrganizationMembers.items.filter(
        (item) => item.userId === acceptingUser.user.id,
      ),
    ).toEqual([
      expect.objectContaining({
        role: "member",
        userId: acceptingUser.user.id,
      }),
    ]);
    const acceptedTeamMembers = await getGeneratedTeamMembers(
      winner!.page.context().request,
      organization.id,
      team.id,
    );
    expect(
      acceptedTeamMembers.items.filter(
        (item) => item.userId === acceptingUser.user.id,
      ),
    ).toEqual([expect.objectContaining({ userId: acceptingUser.user.id })]);

    await confirmLocalRecipient(invitedOwner.page, ownerInvitation.path);
    await expect(
      invitedOwner.page.getByText("Owner", { exact: true }),
    ).toBeVisible();
    await invitedOwner.page
      .getByRole("button", { name: "Accept invitation" })
      .click();
    await expect(invitedOwner.page).toHaveURL(
      `${webOrigin}/w/${encodeURIComponent(
        organization.canonicalKey,
      )}/dashboard`,
    );
    const organizationMembersAfterOwnerAccept =
      await getGeneratedOrganizationMembers(
        invitedOwner.context.request,
        organization.id,
      );
    expect(
      organizationMembersAfterOwnerAccept.items.filter(
        (item) => item.userId === invitedOwnerUser.user.id,
      ),
    ).toEqual([
      expect.objectContaining({
        role: "owner",
        userId: invitedOwnerUser.user.id,
      }),
    ]);
    await page.goto(
      `/w/${encodeURIComponent(organization.canonicalKey)}/settings/users`,
    );
    const invitedOwnerArticle = page.getByRole("row", {
      name: "E2E Invited Owner workspace member",
    });
    await expect(invitedOwnerArticle).toContainText(
      identities.invitedOwner.email,
    );
    await expect(invitedOwnerArticle).toContainText("Owner");
    await expect(
      invitedOwnerArticle.getByRole("combobox", {
        name: "Role for E2E Invited Owner",
      }),
    ).toContainText("Owner");

    await confirmLocalRecipient(rejecting.page, rejectingInvitation.path);
    expect(
      (await getGeneratedAccountInvitations(rejecting.context.request)).items,
    ).toHaveLength(1);
    await rejecting.page
      .getByRole("button", { name: "Reject invitation" })
      .click();
    await expect(
      rejecting.page.getByText("This invitation has been rejected.", {
        exact: true,
      }),
    ).toBeVisible();
    await expect(
      rejecting.page.getByRole("button", { name: "Accept invitation" }),
    ).toHaveCount(0);
    await expect(
      rejecting.page.getByRole("button", { name: "Reject invitation" }),
    ).toHaveCount(0);
    expect(
      (await getGeneratedAccountInvitations(rejecting.context.request)).items,
    ).toHaveLength(0);
    await rejecting.page.goto("/welcome");
    await expect(
      rejecting.page.getByRole("heading", {
        name: "Create your first workspace",
      }),
    ).toBeVisible();

    await outsider.page.goto(acceptingInvitation.path);
    await expect(outsider.page.getByRole("main")).toHaveCount(1);
    await expect(outsider.page.getByRole("main")).toHaveAttribute(
      "id",
      "main-content",
    );
    await expect(
      outsider.page.getByText(
        "This invitation is not available for the current account.",
        { exact: true },
      ),
    ).toBeVisible();
    await expect(
      outsider.page.getByText("E2E Invitation Workspace", { exact: true }),
    ).toHaveCount(0);
    await expect(
      outsider.page.getByText(identities.acceptingInvitee.email, {
        exact: true,
      }),
    ).toHaveCount(0);
    await expect(
      outsider.page.getByText("E2E Invitation Team", { exact: true }),
    ).toHaveCount(0);

    const finalInvitationsPath = `/w/${encodeURIComponent(
      organization.canonicalKey,
    )}/settings/invitations`;
    const finalSessionRefresh = page.waitForResponse((response) => {
      const request = response.request();
      return (
        request.method() === "GET" &&
        new URL(response.url()).pathname === "/api/v1/auth/session"
      );
    });
    const finalRouteRefresh = page.waitForResponse((response) => {
      const request = response.request();
      const url = new URL(response.url());
      const routerState = decodeURIComponent(
        request.headers()["next-router-state-tree"] ?? "",
      );
      return (
        request.method() === "GET" &&
        url.pathname === finalInvitationsPath &&
        url.searchParams.has("_rsc") &&
        request.headers().rsc === "1" &&
        routerState.includes('"refetch"')
      );
    });
    await page.goto(finalInvitationsPath);
    const [, routeRefreshResponse] = await Promise.all([
      finalSessionRefresh,
      finalRouteRefresh,
    ]);
    await routeRefreshResponse.finished();
    await page.evaluate(
      () =>
        new Promise<void>((resolve) => {
          requestAnimationFrame(() => requestAnimationFrame(() => resolve()));
        }),
    );
    await expect(page.locator("html")).toHaveAttribute(
      "data-app-hydrated",
      "true",
    );
    await page.getByLabel("Status", { exact: true }).click();
    await page.getByRole("option", { name: "Expired", exact: true }).click();
    await expect(page.getByLabel("Status", { exact: true })).toContainText(
      "Expired",
    );
    await expect(
      page.getByText("No invitation activity matches this filter.", {
        exact: true,
      }),
    ).toBeVisible();
  });
});
