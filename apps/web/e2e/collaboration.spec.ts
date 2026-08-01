import type { BrowserContext, Page } from "@playwright/test";

import {
  addGeneratedOrganizationMember,
  createGeneratedTeam,
  getGeneratedAccountInvitations,
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
    page.getByRole("heading", { name: "403", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "This page could not be accessed." }),
  ).toBeVisible();
}

async function createInvitationThroughBrowser(
  page: Page,
  options: Readonly<{
    email: string;
    teamName?: string;
  }>,
): Promise<Readonly<{ id: string; path: string }>> {
  const open = page.getByRole("button", { name: "Create invitation" });
  await expect(open).toBeEnabled();
  await open.click();
  await page.getByLabel("Email address").fill(options.email);
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
  expect(url.origin).toBe("http://127.0.0.1:3127");
  expect(url.pathname).toMatch(/^\/invite\/[0-9a-f-]{36}$/u);
  await page.getByRole("button", { name: "Copy invitation link" }).click();
  await expect(
    page.getByText("Invitation link copied.", { exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Close", exact: true }).click();
  return { id: url.pathname.slice("/invite/".length), path: url.pathname };
}

async function confirmLocalRecipient(page: Page, invitationPath: string) {
  await page.goto(invitationPath);
  await expect(
    page.getByText("Verify the invited email address before responding.", {
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
  test.describe.configure({ timeout: 60_000 });
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
    await createTeamButton.click();
    await page.getByLabel("Team name").fill("E2E Platform Team");
    await page
      .getByRole("button", { name: "Create team", exact: true })
      .click();
    await expect(
      page.getByText("Team created.", { exact: true }),
    ).toBeVisible();

    await page
      .getByRole("button", { name: "Rename E2E Platform Team" })
      .click();
    await page.getByLabel("Team name").fill("E2E Core Team");
    await page
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
      "Rename E2E Core Team",
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
    const outsider = await createTrackedContext(
      organizationScenario,
      "mismatch outsider context",
    );
    await accepting.context.grantPermissions([
      "clipboard-read",
      "clipboard-write",
    ]);
    await admin.context.grantPermissions(["clipboard-read", "clipboard-write"]);
    await page
      .context()
      .grantPermissions(["clipboard-read", "clipboard-write"]);

    const [ownerUser, adminUser, memberUser, acceptingUser, rejectingUser] =
      await organizationScenario.createLocalUsers([
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
    const clicks = Promise.all([
      accepting.page.getByRole("button", { name: "Accept invitation" }).click(),
      racePage.getByRole("button", { name: "Accept invitation" }).click(),
    ]);
    await bothIntercepted;
    releaseDecisions();
    await clicks;
    await accepting.context.unroute(acceptPattern);
    const canonicalDashboard = `/w/${organization.canonicalKey}/dashboard`;
    await expect
      .poll(async () => {
        const states = await Promise.all(
          [accepting.page, racePage].map(async (candidate) => ({
            accepted: await candidate
              .getByText("This invitation has been accepted.", { exact: true })
              .isVisible()
              .catch(() => false),
            path: new URL(candidate.url()).pathname,
          })),
        );
        return states.every(
          (state) => state.path === canonicalDashboard || state.accepted,
        );
      })
      .toBe(true);
    await accepting.page.goto(canonicalDashboard);
    await expect(
      accepting.page.getByRole("heading", { name: "Workspace dashboard" }),
    ).toBeVisible();
    await accepting.page.goto(`/w/${organization.canonicalKey}/settings/teams`);
    await expect(
      accepting.page.getByText("E2E Invitation Team", { exact: true }),
    ).toBeVisible();
    await expect(
      accepting.page.getByText(identities.acceptingInvitee.email, {
        exact: true,
      }),
    ).toBeVisible();
    const acceptedTeamMembers = await getGeneratedTeamMembers(
      accepting.context.request,
      organization.id,
      team.id,
    );
    expect(acceptedTeamMembers.items.map((item) => item.userId)).toContain(
      acceptingUser.user.id,
    );

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

    await page.getByLabel("Status", { exact: true }).click();
    await page.getByRole("option", { name: "Expired", exact: true }).click();
    await expect(
      page.getByText("No invitation activity matches this filter.", {
        exact: true,
      }),
    ).toBeVisible();
  });
});
