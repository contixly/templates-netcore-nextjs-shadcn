/** @jest-environment node */

import { readFileSync } from "node:fs";

import type { APIRequestContext } from "@playwright/test";

import {
  createPlaywrightFetch,
  confirmGeneratedLocalAutomationEmail,
} from "@/e2e/support/generated-auth-api";
import {
  addGeneratedOrganizationMember,
  addGeneratedTeamMember,
  createGeneratedInvitation,
  createGeneratedTeam,
  deleteGeneratedTeam,
  getGeneratedAccountInvitations,
  getGeneratedOrganizationMembers,
  getGeneratedTeamMemberCandidates,
  getGeneratedTeams,
  removeGeneratedTeamMember,
  updateGeneratedTeam,
} from "@/e2e/support/generated-collaboration-api";
import {
  finalizeOrganizationTeardown,
  OrganizationTeardownRegistry,
} from "@/e2e/support/organization-e2e-harness";
import type { Client } from "@/src/lib/api/generated/client";
import {
  addOrganizationMember,
  addTeamMember,
  confirmLocalAutomationEmail,
  createInvitation,
  createTeam,
  deleteTeam,
  getAccountInvitations,
  getAuthCsrf,
  getOrganizationMembers,
  getTeamMemberCandidates,
  getTeams,
  removeTeamMember,
  updateTeam,
} from "@/src/lib/api/generated/sdk.gen";

jest.mock("@/src/lib/api/generated/client", () => ({
  createClient: jest.fn(() => ({ transport: "playwright" })),
}));
jest.mock("@/src/lib/api/generated/sdk.gen", () => ({
  addOrganizationMember: jest.fn(),
  addTeamMember: jest.fn(),
  confirmLocalAutomationEmail: jest.fn(),
  createInvitation: jest.fn(),
  createTeam: jest.fn(),
  deleteTeam: jest.fn(),
  getAccountInvitations: jest.fn(),
  getAuthCsrf: jest.fn(),
  getOrganizationMembers: jest.fn(),
  getTeamMemberCandidates: jest.fn(),
  getTeams: jest.fn(),
  removeTeamMember: jest.fn(),
  updateTeam: jest.fn(),
}));

const request = {
  fetch: jest.fn(),
  storageState: jest.fn(async () => ({ cookies: [], origins: [] })),
} as unknown as APIRequestContext;
const client = { transport: "playwright" } as unknown as Client;
const organizationId = "01900000-0000-7000-8000-000000000101";
const teamId = "01900000-0000-7000-8000-000000000201";
const userId = "01900000-0000-7000-8000-000000000301";
const invitationId = "01900000-0000-7000-8000-000000000401";

beforeEach(() => {
  jest.clearAllMocks();
  jest.mocked(getAuthCsrf).mockResolvedValue({
    data: { data: { requestToken: "csrf-token" } },
  } as never);
});

it("reserves cleanup before creation and accounts for each organization id exactly once", async () => {
  const calls: string[] = [];
  const registry = new OrganizationTeardownRegistry();
  const owner = registry.reserveLocalUser("owner", async () => {
    calls.push("cleanup owner");
    return { deletedOrganizations: 1 };
  });

  calls.push("create owner");
  registry.localUserCreated(owner);
  registry.organizationCreated(owner, organizationId);
  expect(() => registry.organizationCreated(owner, organizationId)).toThrow(
    `owner organization ${organizationId} was already accounted as created`,
  );

  await finalizeOrganizationTeardown(registry, { scenarioFailed: false });
  expect(calls).toEqual(["create owner", "cleanup owner"]);
});

it("rejects duplicate deletion accounting instead of hiding teardown drift", () => {
  const registry = new OrganizationTeardownRegistry();
  const owner = registry.trackLocalUser("owner", async () => ({
    deletedOrganizations: 0,
  }));
  registry.organizationCreated(owner, organizationId);
  registry.organizationDeleted(owner, organizationId);

  expect(() => registry.organizationDeleted(owner, organizationId)).toThrow(
    `owner organization ${organizationId} was not accounted as created`,
  );
});

it("closes every context and created identity after a partial setup failure", async () => {
  const calls: string[] = [];
  const registry = new OrganizationTeardownRegistry();
  registry.trackContext("owner context", async () => {
    calls.push("close owner context");
  });
  const owner = registry.reserveLocalUser("owner", async () => {
    calls.push("cleanup owner");
    return { deletedOrganizations: 0 };
  });
  registry.localUserCreated(owner);
  registry.trackContext("invitee context", async () => {
    calls.push("close invitee context");
  });
  registry.reserveLocalUser("invitee", async () => {
    calls.push("cleanup uncreated invitee");
    return { deletedOrganizations: 0 };
  });

  await finalizeOrganizationTeardown(registry, { scenarioFailed: true });

  expect(calls).toEqual([
    "close invitee context",
    "cleanup owner",
    "close owner context",
  ]);
});

it("confirms local email through the generated SDK with CSRF", async () => {
  jest.mocked(confirmLocalAutomationEmail).mockResolvedValue({
    data: { data: { confirmed: true } },
  } as never);

  await expect(confirmGeneratedLocalAutomationEmail(request)).resolves.toEqual({
    confirmed: true,
  });
  expect(confirmLocalAutomationEmail).toHaveBeenCalledWith({
    client,
    headers: { "X-CSRF-TOKEN": "csrf-token" },
  });
});

it("rejects a generated SDK transport request that escapes the configured web origin", async () => {
  const transport = createPlaywrightFetch(request);

  await expect(
    transport("https://attacker.invalid/api/v1/teams"),
  ).rejects.toThrow(
    "E2E SDK request escaped the web origin: https://attacker.invalid.",
  );
  expect(request.fetch).not.toHaveBeenCalled();
});

it("uses generated operations for collaboration setup and returns contract ids and paths", async () => {
  const team = {
    id: teamId,
    organizationId,
    name: "Platform",
    memberCount: 0,
    members: { items: [], nextCursor: null },
    createdAt: "2026-08-01T00:00:00Z",
    updatedAt: "2026-08-01T00:00:00Z",
  };
  const member = {
    id: "01900000-0000-7000-8000-000000000302",
    userId,
    name: "Member User",
    email: "member@local-agent.test",
    imageUrl: null,
    role: "member",
    organizationJoinedAt: "2026-08-01T00:00:00Z",
    teamJoinedAt: "2026-08-01T01:00:00Z",
  };
  const invitation = {
    id: invitationId,
    organizationId,
    organizationName: "Workspace",
    canonicalOrganizationKey: "workspace",
    teamId,
    teamName: "Platform",
    email: "invitee@local-agent.test",
    role: "member",
    status: "pending",
    displayState: "pending",
    expiresAt: "2026-08-03T00:00:00Z",
    createdAt: "2026-08-01T00:00:00Z",
    inviterId: userId,
    inviterName: "Owner User",
    invitationPath: `/invite/${invitationId}`,
  };
  jest
    .mocked(addOrganizationMember)
    .mockResolvedValue({ data: { data: member } } as never);
  jest.mocked(createTeam).mockResolvedValue({ data: { data: team } } as never);
  jest.mocked(updateTeam).mockResolvedValue({
    data: { data: { ...team, name: "Core" } },
  } as never);
  jest
    .mocked(addTeamMember)
    .mockResolvedValue({ data: { data: member } } as never);
  jest.mocked(removeTeamMember).mockResolvedValue({
    data: { data: { teamId, userId } },
  } as never);
  jest
    .mocked(deleteTeam)
    .mockResolvedValue({ data: { data: { teamId } } } as never);
  jest
    .mocked(createInvitation)
    .mockResolvedValue({ data: { data: invitation } } as never);
  jest.mocked(getTeams).mockResolvedValue({
    data: { data: { items: [team], nextCursor: null } },
  } as never);
  jest.mocked(getTeamMemberCandidates).mockResolvedValue({
    data: {
      data: {
        items: [
          {
            ...member,
            memberId: member.id,
            joinedAt: member.organizationJoinedAt,
          },
        ],
        nextCursor: null,
      },
    },
  } as never);
  jest.mocked(getAccountInvitations).mockResolvedValue({
    data: { data: { items: [invitation], nextCursor: null } },
  } as never);
  jest.mocked(getOrganizationMembers).mockResolvedValue({
    data: {
      data: {
        items: [
          {
            id: member.id,
            userId: member.userId,
            name: member.name,
            email: member.email,
            imageUrl: member.imageUrl,
            role: member.role,
            joinedAt: member.organizationJoinedAt,
            emailDomain: "local-agent.test",
            isOutsideAllowedEmailDomains: false,
          },
        ],
        nextCursor: null,
      },
    },
  } as never);

  await expect(
    addGeneratedOrganizationMember(request, organizationId, userId, "member"),
  ).resolves.toMatchObject({ userId });
  await expect(
    createGeneratedTeam(request, organizationId, "Platform"),
  ).resolves.toMatchObject({ id: teamId });
  await expect(
    updateGeneratedTeam(request, organizationId, teamId, "Core"),
  ).resolves.toMatchObject({ name: "Core" });
  await expect(
    addGeneratedTeamMember(request, organizationId, teamId, userId),
  ).resolves.toMatchObject({ userId });
  await expect(
    removeGeneratedTeamMember(request, organizationId, teamId, userId),
  ).resolves.toEqual({ teamId, userId });
  await expect(
    deleteGeneratedTeam(request, organizationId, teamId),
  ).resolves.toEqual({ teamId });
  await expect(
    createGeneratedInvitation(request, organizationId, {
      email: invitation.email,
      role: "member",
      teamId,
    }),
  ).resolves.toMatchObject({
    id: invitationId,
    invitationPath: `/invite/${invitationId}`,
  });
  await expect(
    getGeneratedTeams(request, organizationId),
  ).resolves.toMatchObject({ items: [{ id: teamId }] });
  await expect(
    getGeneratedTeamMemberCandidates(request, organizationId, teamId, "member"),
  ).resolves.toMatchObject({ items: [{ userId }] });
  await expect(
    getGeneratedOrganizationMembers(request, organizationId),
  ).resolves.toMatchObject({ items: [{ userId }] });
  await expect(getGeneratedAccountInvitations(request)).resolves.toMatchObject({
    items: [{ id: invitationId }],
  });

  for (const operation of [
    addOrganizationMember,
    createTeam,
    updateTeam,
    addTeamMember,
    removeTeamMember,
    deleteTeam,
    createInvitation,
  ]) {
    expect(operation).toHaveBeenCalledWith(expect.objectContaining({ client }));
  }
});

it("contains no raw fetch or SQL escape hatch in collaboration E2E support", () => {
  const source = [
    "e2e/support/generated-auth-api.ts",
    "e2e/support/generated-collaboration-api.ts",
    "e2e/support/organization-test-fixture.ts",
  ]
    .map((path) =>
      readFileSync(new URL(`../../${path}`, import.meta.url), "utf8"),
    )
    .join("\n");

  expect(source).not.toMatch(/(^|[^\w.])fetch\s*\(/u);
  expect(source).not.toMatch(
    /\b(SELECT|INSERT|UPDATE|DELETE)\s+(FROM|INTO|SET)\b/iu,
  );
  expect(source).not.toContain("Npgsql");
  expect(source).not.toContain("Prisma");
});
