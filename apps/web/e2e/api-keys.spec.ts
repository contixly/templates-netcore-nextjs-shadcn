import type { BrowserContext } from "@playwright/test";

import {
  callGeneratedApiKeyPrincipal,
  callGeneratedMachineOrganization,
  callGeneratedOrganizationMembers,
  callGeneratedOrganizations,
  callGeneratedTeamMembers,
  callGeneratedTeams,
  createGeneratedOrganizationKey,
  createGeneratedPersonalKey,
  listGeneratedOrganizationApiKeys,
  listGeneratedPersonalApiKeys,
} from "./support/generated-api-keys-api";
import {
  assertApiKeyProblem,
  assertGeneratedApiKeyCreated,
  assertGeneratedSuccess,
  assertNoCredentialEcho,
  assertOpaquePageContinuation,
  assertOrganizationMemberShape,
  assertOrganizationOwnedSummary,
  assertSafeApiKey,
  assertTeamMemberShape,
  assertTeamShape,
  assertUserOrganizationSummary,
  createApiKeyThroughUi,
  editApiKeyNameThroughUi,
  expectApiKeyRow,
  revokeApiKeyThroughUi,
  rotateApiKeyThroughUi,
  toggleApiKeyThroughUi,
  uniqueApiKeyIdentity,
} from "./support/api-key-e2e-harness";
import {
  addGeneratedOrganizationMember,
  addGeneratedTeamMember,
  createGeneratedTeam,
  getGeneratedOrganizationMembers,
  updateGeneratedOrganizationMemberRole,
} from "./support/generated-collaboration-api";
import {
  cleanupLocalAutomationUser,
  createLocalAutomationUser,
  getGeneratedAuthSession,
} from "./support/generated-auth-api";
import { createGeneratedOrganization } from "./support/generated-organizations-api";
import { expect, test } from "./support/organization-test-fixture";

// These scenarios handle reveal-once credentials. No Playwright artifact may
// retain a DOM snapshot, screenshot, video frame, request header, or response.
process.env.PLAYWRIGHT_NO_COPY_PROMPT = "1";
test.use({ screenshot: "off", trace: "off", video: "off" });

const personalApiKeyFields = [
  "createdAt",
  "enabled",
  "expiresAt",
  "id",
  "lastRequestAt",
  "name",
  "ownerId",
  "ownerKind",
  "rateLimitEnabled",
  "rateLimitMax",
  "rateLimitWindow",
  "requestCount",
  "rotatedAt",
  "scopes",
  "start",
  "status",
  "updatedAt",
  "windowStartedAt",
] as const;

async function anonymousContext(
  browser: Readonly<{
    newContext(): Promise<BrowserContext>;
  }>,
) {
  return browser.newContext();
}

test("auth boundary: personal management requires authentication and /me rejects missing, blank, invalid, and cookie-only credentials", async ({
  browser,
  organizationScenario,
  page,
}, testInfo) => {
  const identity = uniqueApiKeyIdentity(testInfo, "personal-auth-boundary");

  await page.goto("/user/api-keys");
  await expect(page).toHaveURL("/auth/login?redirect=%2Fuser%2Fprofile");
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();

  const anonymous = await anonymousContext(browser);
  try {
    const missing = await callGeneratedApiKeyPrincipal(anonymous.request);
    assertApiKeyProblem(missing, {
      code: "api_key_missing",
      instance: "/api/v1/me",
      status: 401,
    });

    const blank = await callGeneratedApiKeyPrincipal(anonymous.request, "   ");
    assertApiKeyProblem(blank, {
      code: "api_key_missing",
      instance: "/api/v1/me",
      status: 401,
    });

    const invalid = await callGeneratedApiKeyPrincipal(
      anonymous.request,
      "not-a-valid-api-key",
    );
    assertApiKeyProblem(invalid, {
      code: "api_key_invalid",
      instance: "/api/v1/me",
      status: 401,
    });

    await organizationScenario.createLocalUser(
      page.context(),
      identity,
      "personal auth-boundary user",
    );
    const cookieOnly = await callGeneratedApiKeyPrincipal(
      page.context().request,
    );
    assertApiKeyProblem(cookieOnly, {
      code: "api_key_missing",
      instance: "/api/v1/me",
      status: 401,
    });
  } finally {
    await anonymous.close();
  }
});

test("personal lifecycle: create, reveal, use, edit, disable-enable, rotate, and revoke invalidate the right credential", async ({
  organizationScenario,
  page,
}, testInfo) => {
  test.setTimeout(120_000);
  const identity = uniqueApiKeyIdentity(testInfo, "personal-lifecycle");
  const scenario = await organizationScenario.createLocalUser(
    page.context(),
    identity,
    "personal lifecycle user",
  );
  let currentCredential = "";
  let retiredCredential = "";

  try {
    await page.goto("/user/api-keys");
    await expect(
      page.getByRole("heading", { level: 1, name: "API keys" }),
    ).toBeVisible();

    currentCredential = await createApiKeyThroughUi(page, {
      name: "E2E personal reader",
      ownerKind: "user",
    });

    const principal = await callGeneratedApiKeyPrincipal(
      page.context().request,
      currentCredential,
    );
    const me = assertGeneratedSuccess(principal, 200);
    expect(Object.keys(me).sort()).toEqual(["key", "principal", "scopes"]);
    expect(Object.keys(me.key).sort()).toEqual(["configId", "id", "start"]);
    expect(Object.keys(me.principal).sort()).toEqual([
      "organizationId",
      "ownerKind",
      "userId",
    ]);
    expect(me.principal).toEqual({
      ownerKind: "user",
      userId: scenario.user.id,
      organizationId: null,
    });
    expect(me.key.configId).toBe("user-keys");
    expect(me.scopes).toEqual(["basic:read"]);
    assertNoCredentialEcho(me, currentCredential);

    const createdList = await listGeneratedPersonalApiKeys(
      page.context().request,
      { limit: 50 },
    );
    const createdPage = assertGeneratedSuccess(createdList, 200);
    expect(createdPage.nextCursor).toBeNull();
    expect(createdPage.items).toHaveLength(1);
    const createdKey = createdPage.items[0];
    expect(createdKey.id).toBe(me.key.id);
    expect(createdKey.name).toBe("E2E personal reader");
    expect(createdKey.ownerKind).toBe("user");
    expect(createdKey.ownerId).toBe(scenario.user.id);
    expect(Object.keys(createdKey).sort()).toEqual(personalApiKeyFields);
    assertSafeApiKey(createdKey);
    assertNoCredentialEcho(createdPage, currentCredential);

    await editApiKeyNameThroughUi(
      page,
      "E2E personal reader",
      "E2E personal renamed",
    );
    await expectApiKeyRow(page, "E2E personal renamed", "Active");
    const renamedPage = assertGeneratedSuccess(
      await listGeneratedPersonalApiKeys(page.context().request, { limit: 50 }),
      200,
    );
    expect(renamedPage.items[0].name).toBe("E2E personal renamed");
    assertSafeApiKey(renamedPage.items[0]);

    await toggleApiKeyThroughUi(page, "E2E personal renamed", "Disable");
    await expectApiKeyRow(page, "E2E personal renamed", "Disabled");
    const disabled = await callGeneratedApiKeyPrincipal(
      page.context().request,
      currentCredential,
    );
    assertApiKeyProblem(disabled, {
      code: "api_key_invalid",
      credential: currentCredential,
      instance: "/api/v1/me",
      status: 401,
    });

    await toggleApiKeyThroughUi(page, "E2E personal renamed", "Enable");
    await expectApiKeyRow(page, "E2E personal renamed", "Active");
    expect(
      assertGeneratedSuccess(
        await callGeneratedApiKeyPrincipal(
          page.context().request,
          currentCredential,
        ),
        200,
      ).key.id,
    ).toBe(me.key.id);

    retiredCredential = currentCredential;
    currentCredential = await rotateApiKeyThroughUi(
      page,
      "E2E personal renamed",
      "user",
    );
    assertApiKeyProblem(
      await callGeneratedApiKeyPrincipal(
        page.context().request,
        retiredCredential,
      ),
      {
        code: "api_key_invalid",
        credential: retiredCredential,
        instance: "/api/v1/me",
        status: 401,
      },
    );
    retiredCredential = "";

    const rotatedMe = assertGeneratedSuccess(
      await callGeneratedApiKeyPrincipal(
        page.context().request,
        currentCredential,
      ),
      200,
    );
    expect(rotatedMe.key.id).toBe(me.key.id);
    expect(rotatedMe.key.start).not.toBe(me.key.start);
    assertNoCredentialEcho(rotatedMe, currentCredential);
    const rotatedPage = assertGeneratedSuccess(
      await listGeneratedPersonalApiKeys(page.context().request, { limit: 50 }),
      200,
    );
    expect(rotatedPage.items[0].rotatedAt).not.toBeNull();
    assertSafeApiKey(rotatedPage.items[0]);
    assertNoCredentialEcho(rotatedPage, currentCredential);

    await revokeApiKeyThroughUi(page, "E2E personal renamed");
    await expect(
      page.getByRole("row").filter({ hasText: "E2E personal renamed" }),
    ).toHaveCount(0);
    assertApiKeyProblem(
      await callGeneratedApiKeyPrincipal(
        page.context().request,
        currentCredential,
      ),
      {
        code: "api_key_invalid",
        credential: currentCredential,
        instance: "/api/v1/me",
        status: 401,
      },
    );
    currentCredential = "";
    const afterRevoke = assertGeneratedSuccess(
      await listGeneratedPersonalApiKeys(page.context().request, { limit: 50 }),
      200,
    );
    expect(afterRevoke).toEqual({ items: [], nextCursor: null });
  } finally {
    currentCredential = "";
    retiredCredential = "";
  }
});

test("organization management: owner and admin manage separated organization keys while a member has no navigation or direct access", async ({
  organizationScenario,
  page,
}, testInfo) => {
  test.setTimeout(120_000);
  const adminContext = await organizationScenario.createContext(
    "API-key management admin context",
  );
  const memberContext = await organizationScenario.createContext(
    "API-key management member context",
  );
  const adminPage = await adminContext.newPage();
  const memberPage = await memberContext.newPage();
  const [owner, admin, member] = await organizationScenario.createLocalUsers([
    organizationScenario.prepareLocalUser(
      page.context(),
      uniqueApiKeyIdentity(testInfo, "organization-management-owner"),
      "API-key management owner",
    ),
    organizationScenario.prepareLocalUser(
      adminContext,
      uniqueApiKeyIdentity(testInfo, "organization-management-admin"),
      "API-key management admin",
    ),
    organizationScenario.prepareLocalUser(
      memberContext,
      uniqueApiKeyIdentity(testInfo, "organization-management-member"),
      "API-key management member",
    ),
  ]);
  const organization = await organizationScenario.createOrganization(
    owner,
    page.context().request,
    `API Key Management ${testInfo.workerIndex}`,
  );
  await addGeneratedOrganizationMember(
    page.context().request,
    organization.id,
    admin.user.id,
    "admin",
  );
  await addGeneratedOrganizationMember(
    page.context().request,
    organization.id,
    member.user.id,
    "member",
  );
  let ownerCredential = "";
  let adminCredential = "";
  let personalCredential = "";

  try {
    await page.goto(`/w/${organization.canonicalKey}/settings/api-keys`);
    await expect(
      page.getByRole("heading", { level: 1, name: "API keys" }),
    ).toBeVisible();
    await expect(
      page
        .getByRole("navigation", { name: "Workspace settings" })
        .getByRole("link", { name: "API keys" }),
    ).toBeVisible();
    ownerCredential = await createApiKeyThroughUi(page, {
      name: "Owner managed key",
      ownerKind: "organization",
    });

    await adminPage.goto(`/w/${organization.canonicalKey}/settings/api-keys`);
    await expect(
      adminPage.getByRole("heading", { level: 1, name: "API keys" }),
    ).toBeVisible();
    await expect(
      adminPage
        .getByRole("navigation", { name: "Workspace settings" })
        .getByRole("link", { name: "API keys" }),
    ).toBeVisible();
    adminCredential = await createApiKeyThroughUi(adminPage, {
      name: "Admin managed key",
      ownerKind: "organization",
    });

    const ownerMachinePage = assertGeneratedSuccess(
      await callGeneratedOrganizations(
        page.context().request,
        ownerCredential,
        { limit: 50 },
      ),
      200,
    );
    expect(ownerMachinePage.items).toHaveLength(1);
    assertOrganizationOwnedSummary(ownerMachinePage.items[0], organization.id);
    assertNoCredentialEcho(ownerMachinePage, ownerCredential);

    const adminMachinePage = assertGeneratedSuccess(
      await callGeneratedOrganizations(adminContext.request, adminCredential, {
        limit: 50,
      }),
      200,
    );
    expect(adminMachinePage.items).toHaveLength(1);
    assertOrganizationOwnedSummary(adminMachinePage.items[0], organization.id);
    assertNoCredentialEcho(adminMachinePage, adminCredential);

    const personal = await createGeneratedPersonalKey(page.context().request, {
      name: "Separated personal key",
      presetIds: ["basic-read"],
    });
    assertGeneratedApiKeyCreated(personal, "/api/v1/account/api-keys");
    personalCredential = personal.credential;
    expect(personal.apiKey.ownerKind).toBe("user");
    expect(personal.apiKey.ownerId).toBe(owner.user.id);
    assertSafeApiKey(personal.apiKey);

    const personalList = assertGeneratedSuccess(
      await listGeneratedPersonalApiKeys(page.context().request, { limit: 50 }),
      200,
    );
    expect(personalList.items.map((item) => item.id)).toEqual([
      personal.apiKey.id,
    ]);
    assertNoCredentialEcho(personalList, personalCredential);

    const organizationList = assertGeneratedSuccess(
      await listGeneratedOrganizationApiKeys(
        page.context().request,
        organization.id,
        { limit: 50 },
      ),
      200,
    );
    expect(new Set(organizationList.items.map((item) => item.name))).toEqual(
      new Set(["Owner managed key", "Admin managed key"]),
    );
    expect(
      organizationList.items.some((item) => item.id === personal.apiKey.id),
    ).toBe(false);
    for (const item of organizationList.items) {
      expect(item.ownerKind).toBe("organization");
      expect(item.ownerId).toBe(organization.id);
      assertSafeApiKey(item);
    }
    assertNoCredentialEcho(organizationList, ownerCredential);
    assertNoCredentialEcho(organizationList, adminCredential);

    await memberPage.goto(`/w/${organization.canonicalKey}/settings/workspace`);
    await expect(
      memberPage
        .getByRole("navigation", { name: "Workspace settings" })
        .getByRole("link", { name: "API keys" }),
    ).toHaveCount(0);
    assertApiKeyProblem(
      await listGeneratedOrganizationApiKeys(
        memberContext.request,
        organization.id,
        { limit: 50 },
      ),
      {
        code: "api_key_permission_denied",
        instance: `/api/v1/organizations/${organization.id}/api-keys`,
        status: 403,
      },
    );
    await memberPage.goto(`/w/${organization.canonicalKey}/settings/api-keys`);
    await expect(
      memberPage.getByRole("heading", {
        level: 1,
        name: "API keys are unavailable",
      }),
    ).toBeVisible();
    await expect(
      memberPage.getByRole("button", { name: "Create API key" }),
    ).toHaveCount(0);
  } finally {
    ownerCredential = "";
    adminCredential = "";
    personalCredential = "";
  }
});

test("scope boundary: personal read-all follows current membership while insufficient scopes consume and deny exactly", async ({
  organizationScenario,
  page,
}, testInfo) => {
  test.setTimeout(120_000);
  const memberContext = await organizationScenario.createContext(
    "personal membership-key context",
  );
  const [owner, keyOwner] = await organizationScenario.createLocalUsers([
    organizationScenario.prepareLocalUser(
      page.context(),
      uniqueApiKeyIdentity(testInfo, "scope-membership-owner"),
      "scope membership owner",
    ),
    organizationScenario.prepareLocalUser(
      memberContext,
      uniqueApiKeyIdentity(testInfo, "scope-membership-key-owner"),
      "scope membership key owner",
    ),
  ]);
  const allowed = await organizationScenario.createOrganization(
    owner,
    page.context().request,
    `Scope Allowed ${testInfo.workerIndex}`,
  );
  const foreign = await organizationScenario.createOrganization(
    owner,
    page.context().request,
    `Scope Foreign ${testInfo.workerIndex}`,
  );
  await addGeneratedOrganizationMember(
    page.context().request,
    allowed.id,
    keyOwner.user.id,
    "member",
  );
  let readAllCredential = "";
  let basicCredential = "";
  let organizationReadCredential = "";

  try {
    const readAll = await createGeneratedPersonalKey(memberContext.request, {
      name: "Membership read all",
      presetIds: ["organization-read-all"],
    });
    assertGeneratedApiKeyCreated(readAll, "/api/v1/account/api-keys");
    readAllCredential = readAll.credential;
    const organizations = assertGeneratedSuccess(
      await callGeneratedOrganizations(
        memberContext.request,
        readAllCredential,
        { limit: 50 },
      ),
      200,
    );
    expect(organizations.nextCursor).toBeNull();
    expect(organizations.items).toHaveLength(1);
    assertUserOrganizationSummary(organizations.items[0], {
      id: allowed.id,
      role: "member",
    });

    const detail = assertGeneratedSuccess(
      await callGeneratedMachineOrganization(
        memberContext.request,
        readAllCredential,
        allowed.id,
      ),
      200,
    );
    assertUserOrganizationSummary(detail, {
      id: allowed.id,
      role: "member",
    });
    const members = assertGeneratedSuccess(
      await callGeneratedOrganizationMembers(
        memberContext.request,
        readAllCredential,
        allowed.id,
        { limit: 50 },
      ),
      200,
    );
    expect(members.items).toHaveLength(2);
    for (const item of members.items) assertOrganizationMemberShape(item);

    assertApiKeyProblem(
      await callGeneratedMachineOrganization(
        memberContext.request,
        readAllCredential,
        foreign.id,
      ),
      {
        code: "organization_access_denied",
        credential: readAllCredential,
        instance: `/api/v1/organizations/${foreign.id}`,
        status: 403,
      },
    );

    const basic = await createGeneratedPersonalKey(memberContext.request, {
      name: "Basic scope only",
      presetIds: ["basic-read"],
    });
    assertGeneratedApiKeyCreated(basic, "/api/v1/account/api-keys");
    basicCredential = basic.credential;
    assertApiKeyProblem(
      await callGeneratedOrganizations(memberContext.request, basicCredential, {
        limit: 50,
      }),
      {
        code: "api_key_permission_denied",
        credential: basicCredential,
        instance: "/api/v1/organizations",
        status: 403,
      },
    );

    const organizationRead = await createGeneratedPersonalKey(
      memberContext.request,
      {
        name: "Organization scope only",
        presetIds: ["organization-read"],
      },
    );
    assertGeneratedApiKeyCreated(organizationRead, "/api/v1/account/api-keys");
    organizationReadCredential = organizationRead.credential;
    assertGeneratedSuccess(
      await callGeneratedOrganizations(
        memberContext.request,
        organizationReadCredential,
        { limit: 50 },
      ),
      200,
    );
    assertGeneratedSuccess(
      await callGeneratedMachineOrganization(
        memberContext.request,
        organizationReadCredential,
        allowed.id,
      ),
      200,
    );
    assertApiKeyProblem(
      await callGeneratedOrganizationMembers(
        memberContext.request,
        organizationReadCredential,
        allowed.id,
        { limit: 50 },
      ),
      {
        code: "api_key_permission_denied",
        credential: organizationReadCredential,
        instance: `/api/v1/organizations/${allowed.id}/members`,
        status: 403,
      },
    );

    const managed = assertGeneratedSuccess(
      await listGeneratedPersonalApiKeys(memberContext.request, { limit: 50 }),
      200,
    );
    expect(
      managed.items.find((item) => item.id === basic.apiKey.id)?.requestCount,
    ).toBe(1);
    expect(
      managed.items.find((item) => item.id === organizationRead.apiKey.id)
        ?.requestCount,
    ).toBe(3);

    await organizationScenario.deleteOrganization(
      owner,
      page.context().request,
      allowed,
    );
    assertApiKeyProblem(
      await callGeneratedMachineOrganization(
        memberContext.request,
        readAllCredential,
        allowed.id,
      ),
      {
        code: "organization_access_denied",
        credential: readAllCredential,
        instance: `/api/v1/organizations/${allowed.id}`,
        status: 403,
      },
    );
    const afterMembershipLoss = assertGeneratedSuccess(
      await callGeneratedOrganizations(
        memberContext.request,
        readAllCredential,
        { limit: 50 },
      ),
      200,
    );
    expect(afterMembershipLoss).toEqual({ items: [], nextCursor: null });
    assertNoCredentialEcho(afterMembershipLoss, readAllCredential);
  } finally {
    readAllCredential = "";
    basicCredential = "";
    organizationReadCredential = "";
  }
});

test("organization scope: owner isolation, creator downgrade-removal survival, team redaction, dedicated members, and foreign non-disclosure", async ({
  organizationScenario,
  page,
}, testInfo) => {
  test.setTimeout(120_000);
  const creatorContext = await organizationScenario.createContext(
    "organization-key creator context",
  );
  const memberContext = await organizationScenario.createContext(
    "organization-key team-member context",
  );
  const keeper = await organizationScenario.createLocalUser(
    page.context(),
    uniqueApiKeyIdentity(testInfo, "organization-key-keeper"),
    "organization-key remaining owner",
  );
  const member = await organizationScenario.createLocalUser(
    memberContext,
    uniqueApiKeyIdentity(testInfo, "organization-key-team-member"),
    "organization-key team member",
  );
  const creatorIdentity = uniqueApiKeyIdentity(
    testInfo,
    "organization-key-creator",
  );
  const creator = await createLocalAutomationUser(
    creatorContext.request,
    creatorIdentity,
  );
  const ownerOrganization = await createGeneratedOrganization(
    creatorContext.request,
    `Organization Key Owner ${testInfo.workerIndex}`,
  );
  await addGeneratedOrganizationMember(
    creatorContext.request,
    ownerOrganization.id,
    keeper.user.id,
    "owner",
  );
  organizationScenario.organizationCreated(keeper, ownerOrganization.id);
  await addGeneratedOrganizationMember(
    creatorContext.request,
    ownerOrganization.id,
    member.user.id,
    "member",
  );
  const ownerTeam = await createGeneratedTeam(
    creatorContext.request,
    ownerOrganization.id,
    `Owner Team ${testInfo.workerIndex}`,
  );
  await addGeneratedTeamMember(
    creatorContext.request,
    ownerOrganization.id,
    ownerTeam.id,
    member.user.id,
  );
  const foreignOrganization = await organizationScenario.createOrganization(
    keeper,
    page.context().request,
    `Organization Key Foreign ${testInfo.workerIndex}`,
  );
  const foreignTeam = await createGeneratedTeam(
    page.context().request,
    foreignOrganization.id,
    `Foreign Team ${testInfo.workerIndex}`,
  );
  let allScopesCredential = "";
  let teamOnlyCredential = "";
  let organizationOnlyCredential = "";
  let creatorRemoved = false;

  try {
    const allScopes = await createGeneratedOrganizationKey(
      creatorContext.request,
      ownerOrganization.id,
      {
        name: "Surviving organization key",
        presetIds: ["basic-read", "organization-read-all"],
      },
    );
    assertGeneratedApiKeyCreated(
      allScopes,
      `/api/v1/organizations/${ownerOrganization.id}/api-keys`,
    );
    allScopesCredential = allScopes.credential;
    const teamOnly = await createGeneratedOrganizationKey(
      creatorContext.request,
      ownerOrganization.id,
      {
        name: "Team list scope key",
        presetIds: ["organization-teams-read"],
      },
    );
    assertGeneratedApiKeyCreated(
      teamOnly,
      `/api/v1/organizations/${ownerOrganization.id}/api-keys`,
    );
    teamOnlyCredential = teamOnly.credential;
    const organizationOnly = await createGeneratedOrganizationKey(
      creatorContext.request,
      ownerOrganization.id,
      {
        name: "Organization only key",
        presetIds: ["organization-read"],
      },
    );
    assertGeneratedApiKeyCreated(
      organizationOnly,
      `/api/v1/organizations/${ownerOrganization.id}/api-keys`,
    );
    organizationOnlyCredential = organizationOnly.credential;

    const me = assertGeneratedSuccess(
      await callGeneratedApiKeyPrincipal(
        creatorContext.request,
        allScopesCredential,
      ),
      200,
    );
    expect(me.principal).toEqual({
      ownerKind: "organization",
      organizationId: ownerOrganization.id,
      userId: null,
    });
    expect(me.key.configId).toBe("org-keys");
    expect(me.scopes).toEqual([
      "basic:read",
      "organization:read",
      "member:read",
      "team:read",
      "teamMember:read",
    ]);
    assertNoCredentialEcho(me, allScopesCredential);

    assertGeneratedSuccess(
      await callGeneratedMachineOrganization(
        creatorContext.request,
        allScopesCredential,
        ownerOrganization.id,
      ),
      200,
    );
    const creatorMembership = (
      await getGeneratedOrganizationMembers(
        page.context().request,
        ownerOrganization.id,
      )
    ).items.find((item) => item.userId === creator.user.id);
    if (!creatorMembership) {
      throw new Error("Organization-key creator membership was unavailable.");
    }
    await updateGeneratedOrganizationMemberRole(
      page.context().request,
      ownerOrganization.id,
      creatorMembership.id,
      "member",
    );
    assertGeneratedSuccess(
      await callGeneratedMachineOrganization(
        creatorContext.request,
        allScopesCredential,
        ownerOrganization.id,
      ),
      200,
    );

    const creatorCleanup = await cleanupLocalAutomationUser(
      creatorContext.request,
    );
    expect(Number(creatorCleanup.deletedOrganizations)).toBe(0);
    creatorRemoved = true;

    const organizationPage = assertGeneratedSuccess(
      await callGeneratedOrganizations(
        page.context().request,
        allScopesCredential,
        { limit: 50 },
      ),
      200,
    );
    expect(organizationPage.nextCursor).toBeNull();
    expect(organizationPage.items).toHaveLength(1);
    assertOrganizationOwnedSummary(
      organizationPage.items[0],
      ownerOrganization.id,
    );
    const detail = assertGeneratedSuccess(
      await callGeneratedMachineOrganization(
        page.context().request,
        allScopesCredential,
        ownerOrganization.id,
      ),
      200,
    );
    assertOrganizationOwnedSummary(detail, ownerOrganization.id);

    const organizationMembers = assertGeneratedSuccess(
      await callGeneratedOrganizationMembers(
        page.context().request,
        allScopesCredential,
        ownerOrganization.id,
        { limit: 50 },
      ),
      200,
    );
    expect(organizationMembers.items).toHaveLength(2);
    for (const item of organizationMembers.items) {
      assertOrganizationMemberShape(item);
    }

    const redactedTeams = assertGeneratedSuccess(
      await callGeneratedTeams(
        page.context().request,
        teamOnlyCredential,
        ownerOrganization.id,
        { limit: 50 },
      ),
      200,
    );
    expect(redactedTeams.items).toHaveLength(1);
    assertTeamShape(redactedTeams.items[0], {
      memberCount: 1,
      membersIncluded: false,
      organizationId: ownerOrganization.id,
    });
    expect(redactedTeams.items[0].members).toEqual({
      items: [],
      nextCursor: null,
    });
    assertApiKeyProblem(
      await callGeneratedTeamMembers(
        page.context().request,
        teamOnlyCredential,
        ownerOrganization.id,
        ownerTeam.id,
        { limit: 50 },
      ),
      {
        code: "api_key_permission_denied",
        credential: teamOnlyCredential,
        instance: `/api/v1/organizations/${ownerOrganization.id}/teams/${ownerTeam.id}/members`,
        status: 403,
      },
    );

    const includedTeams = assertGeneratedSuccess(
      await callGeneratedTeams(
        page.context().request,
        allScopesCredential,
        ownerOrganization.id,
        { limit: 50 },
      ),
      200,
    );
    expect(includedTeams.items).toHaveLength(1);
    assertTeamShape(includedTeams.items[0], {
      memberCount: 1,
      membersIncluded: true,
      organizationId: ownerOrganization.id,
    });
    expect(includedTeams.items[0].members.items).toHaveLength(1);
    assertTeamMemberShape(includedTeams.items[0].members.items[0]);
    const dedicatedMembers = assertGeneratedSuccess(
      await callGeneratedTeamMembers(
        page.context().request,
        allScopesCredential,
        ownerOrganization.id,
        ownerTeam.id,
        { limit: 50 },
      ),
      200,
    );
    expect(dedicatedMembers.items).toHaveLength(1);
    expect(dedicatedMembers.items[0].userId).toBe(member.user.id);
    assertTeamMemberShape(dedicatedMembers.items[0]);

    assertApiKeyProblem(
      await callGeneratedTeams(
        page.context().request,
        organizationOnlyCredential,
        ownerOrganization.id,
        { limit: 50 },
      ),
      {
        code: "api_key_permission_denied",
        credential: organizationOnlyCredential,
        instance: `/api/v1/organizations/${ownerOrganization.id}/teams`,
        status: 403,
      },
    );
    assertApiKeyProblem(
      await callGeneratedTeams(
        page.context().request,
        allScopesCredential,
        foreignOrganization.id,
        { limit: 50 },
      ),
      {
        code: "organization_access_denied",
        credential: allScopesCredential,
        instance: `/api/v1/organizations/${foreignOrganization.id}/teams`,
        status: 403,
      },
    );
    assertApiKeyProblem(
      await callGeneratedTeamMembers(
        page.context().request,
        allScopesCredential,
        ownerOrganization.id,
        foreignTeam.id,
        { limit: 50 },
      ),
      {
        code: "team_not_found",
        credential: allScopesCredential,
        instance: `/api/v1/organizations/${ownerOrganization.id}/teams/${foreignTeam.id}/members`,
        status: 404,
      },
    );

    const managedKeys = assertGeneratedSuccess(
      await listGeneratedOrganizationApiKeys(
        page.context().request,
        ownerOrganization.id,
        { limit: 50 },
      ),
      200,
    );
    expect(new Set(managedKeys.items.map((item) => item.id))).toEqual(
      new Set([
        allScopes.apiKey.id,
        teamOnly.apiKey.id,
        organizationOnly.apiKey.id,
      ]),
    );
    for (const item of managedKeys.items) assertSafeApiKey(item);
    assertNoCredentialEcho(managedKeys, allScopesCredential);
    assertNoCredentialEcho(managedKeys, teamOnlyCredential);
    assertNoCredentialEcho(managedKeys, organizationOnlyCredential);
  } finally {
    allScopesCredential = "";
    teamOnlyCredential = "";
    organizationOnlyCredential = "";
    if (!creatorRemoved) {
      const session = await getGeneratedAuthSession(creatorContext.request);
      if (session.authenticated) {
        await cleanupLocalAutomationUser(creatorContext.request);
      }
    }
  }
});

test("pagination: organization, member, team, team-member, and management cursors continue opaquely without duplicates", async ({
  organizationScenario,
  page,
}, testInfo) => {
  test.setTimeout(120_000);
  const firstMemberContext = await organizationScenario.createContext(
    "pagination first member context",
  );
  const secondMemberContext = await organizationScenario.createContext(
    "pagination second member context",
  );
  const [owner, firstMember, secondMember] =
    await organizationScenario.createLocalUsers([
      organizationScenario.prepareLocalUser(
        page.context(),
        uniqueApiKeyIdentity(testInfo, "pagination-owner"),
        "pagination owner",
      ),
      organizationScenario.prepareLocalUser(
        firstMemberContext,
        uniqueApiKeyIdentity(testInfo, "pagination-first-member"),
        "pagination first member",
      ),
      organizationScenario.prepareLocalUser(
        secondMemberContext,
        uniqueApiKeyIdentity(testInfo, "pagination-second-member"),
        "pagination second member",
      ),
    ]);
  const firstOrganization = await organizationScenario.createOrganization(
    owner,
    page.context().request,
    `Pagination Primary ${testInfo.workerIndex}`,
  );
  await organizationScenario.createOrganization(
    owner,
    page.context().request,
    `Pagination Secondary ${testInfo.workerIndex}`,
  );
  for (const scenario of [firstMember, secondMember]) {
    await addGeneratedOrganizationMember(
      page.context().request,
      firstOrganization.id,
      scenario.user.id,
      "member",
    );
  }
  const firstTeam = await createGeneratedTeam(
    page.context().request,
    firstOrganization.id,
    `Pagination Team A ${testInfo.workerIndex}`,
  );
  await createGeneratedTeam(
    page.context().request,
    firstOrganization.id,
    `Pagination Team B ${testInfo.workerIndex}`,
  );
  for (const scenario of [firstMember, secondMember]) {
    await addGeneratedTeamMember(
      page.context().request,
      firstOrganization.id,
      firstTeam.id,
      scenario.user.id,
    );
  }
  let readAllCredential = "";
  let secondCredential = "";

  try {
    const readAll = await createGeneratedPersonalKey(page.context().request, {
      name: "Pagination read all",
      presetIds: ["organization-read-all"],
    });
    assertGeneratedApiKeyCreated(readAll, "/api/v1/account/api-keys");
    readAllCredential = readAll.credential;
    const secondKey = await createGeneratedPersonalKey(page.context().request, {
      name: "Pagination second key",
      presetIds: ["basic-read"],
    });
    assertGeneratedApiKeyCreated(secondKey, "/api/v1/account/api-keys");
    secondCredential = secondKey.credential;

    const firstOrganizations = assertGeneratedSuccess(
      await callGeneratedOrganizations(
        page.context().request,
        readAllCredential,
        { limit: 1 },
      ),
      200,
    );
    const nextOrganizations = assertGeneratedSuccess(
      await callGeneratedOrganizations(
        page.context().request,
        readAllCredential,
        {
          cursor: assertOpaquePageContinuation(firstOrganizations),
          limit: 1,
        },
      ),
      200,
    );
    expect(firstOrganizations.items).toHaveLength(1);
    expect(nextOrganizations.items).toHaveLength(1);
    expect(nextOrganizations.items[0].id).not.toBe(
      firstOrganizations.items[0].id,
    );
    assertUserOrganizationSummary(firstOrganizations.items[0], {
      role: "owner",
    });
    assertUserOrganizationSummary(nextOrganizations.items[0], {
      role: "owner",
    });

    const firstMembers = assertGeneratedSuccess(
      await callGeneratedOrganizationMembers(
        page.context().request,
        readAllCredential,
        firstOrganization.id,
        { limit: 1 },
      ),
      200,
    );
    const nextMembers = assertGeneratedSuccess(
      await callGeneratedOrganizationMembers(
        page.context().request,
        readAllCredential,
        firstOrganization.id,
        {
          cursor: assertOpaquePageContinuation(firstMembers),
          limit: 1,
        },
      ),
      200,
    );
    expect(firstMembers.items).toHaveLength(1);
    expect(nextMembers.items).toHaveLength(1);
    expect(nextMembers.items[0].id).not.toBe(firstMembers.items[0].id);
    assertOrganizationMemberShape(firstMembers.items[0]);
    assertOrganizationMemberShape(nextMembers.items[0]);

    const firstTeams = assertGeneratedSuccess(
      await callGeneratedTeams(
        page.context().request,
        readAllCredential,
        firstOrganization.id,
        { limit: 1 },
      ),
      200,
    );
    const nextTeams = assertGeneratedSuccess(
      await callGeneratedTeams(
        page.context().request,
        readAllCredential,
        firstOrganization.id,
        {
          cursor: assertOpaquePageContinuation(firstTeams),
          limit: 1,
        },
      ),
      200,
    );
    expect(firstTeams.items).toHaveLength(1);
    expect(nextTeams.items).toHaveLength(1);
    expect(nextTeams.items[0].id).not.toBe(firstTeams.items[0].id);
    for (const item of [...firstTeams.items, ...nextTeams.items]) {
      assertTeamShape(item, {
        membersIncluded: true,
        organizationId: firstOrganization.id,
      });
    }

    const firstTeamMembers = assertGeneratedSuccess(
      await callGeneratedTeamMembers(
        page.context().request,
        readAllCredential,
        firstOrganization.id,
        firstTeam.id,
        { limit: 1 },
      ),
      200,
    );
    const nextTeamMembers = assertGeneratedSuccess(
      await callGeneratedTeamMembers(
        page.context().request,
        readAllCredential,
        firstOrganization.id,
        firstTeam.id,
        {
          cursor: assertOpaquePageContinuation(firstTeamMembers),
          limit: 1,
        },
      ),
      200,
    );
    expect(firstTeamMembers.items).toHaveLength(1);
    expect(nextTeamMembers.items).toHaveLength(1);
    expect(nextTeamMembers.items[0].id).not.toBe(firstTeamMembers.items[0].id);
    assertTeamMemberShape(firstTeamMembers.items[0]);
    assertTeamMemberShape(nextTeamMembers.items[0]);

    const firstKeys = assertGeneratedSuccess(
      await listGeneratedPersonalApiKeys(page.context().request, { limit: 1 }),
      200,
    );
    const nextKeys = assertGeneratedSuccess(
      await listGeneratedPersonalApiKeys(page.context().request, {
        cursor: assertOpaquePageContinuation(firstKeys),
        limit: 1,
      }),
      200,
    );
    expect(firstKeys.items).toHaveLength(1);
    expect(nextKeys.items).toHaveLength(1);
    expect(nextKeys.items[0].id).not.toBe(firstKeys.items[0].id);
    assertSafeApiKey(firstKeys.items[0]);
    assertSafeApiKey(nextKeys.items[0]);
    assertNoCredentialEcho(firstKeys, readAllCredential);
    assertNoCredentialEcho(firstKeys, secondCredential);
    assertNoCredentialEcho(nextKeys, readAllCredential);
    assertNoCredentialEcho(nextKeys, secondCredential);
  } finally {
    readAllCredential = "";
    secondCredential = "";
  }
});
