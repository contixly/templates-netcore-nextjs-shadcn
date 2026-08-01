/** @jest-environment node */

import { getAuthCsrfToken } from "@/src/lib/api/auth/browser/get-auth-csrf";
import {
  acceptBrowserInvitation,
  addBrowserTeamMember,
  createBrowserInvitation,
  createBrowserTeam,
  deleteBrowserTeam,
  rejectBrowserInvitation,
  removeBrowserTeamMember,
  updateBrowserTeam,
} from "@/src/lib/api/collaboration/browser/collaboration-mutations";
import { loadAccountInvitations } from "@/src/lib/api/collaboration/server/load-account-invitations";
import { loadInvitationDecision } from "@/src/lib/api/collaboration/server/load-invitation-decision";
import { loadOrganizationInvitations } from "@/src/lib/api/collaboration/server/load-organization-invitations";
import { loadTeamMemberCandidates } from "@/src/lib/api/collaboration/server/load-team-member-candidates";
import { loadTeamMembers } from "@/src/lib/api/collaboration/server/load-team-members";
import { loadTeams } from "@/src/lib/api/collaboration/server/load-teams";
import type { Client } from "@/src/lib/api/generated/client";
import {
  acceptInvitation,
  addTeamMember,
  createInvitation,
  createTeam,
  deleteTeam,
  getAccountInvitations,
  getInvitationDecision,
  getOrganizationInvitations,
  getTeamMemberCandidates,
  getTeamMembers,
  getTeams,
  rejectInvitation,
  removeTeamMember,
  updateTeam,
} from "@/src/lib/api/generated/sdk.gen";
import type {
  InvitationResponse,
  TeamMemberResponse,
  TeamResponse,
} from "@/src/lib/api/generated/types.gen";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

jest.mock("@/src/lib/api/generated/sdk.gen", () => ({
  acceptInvitation: jest.fn(),
  addTeamMember: jest.fn(),
  createInvitation: jest.fn(),
  createTeam: jest.fn(),
  deleteTeam: jest.fn(),
  getAccountInvitations: jest.fn(),
  getInvitationDecision: jest.fn(),
  getOrganizationInvitations: jest.fn(),
  getTeamMemberCandidates: jest.fn(),
  getTeamMembers: jest.fn(),
  getTeams: jest.fn(),
  rejectInvitation: jest.fn(),
  removeTeamMember: jest.fn(),
  updateTeam: jest.fn(),
}));
jest.mock("@/src/lib/api/auth/browser/get-auth-csrf", () => ({
  getAuthCsrfToken: jest.fn(),
}));
jest.mock("@/src/lib/api/server/client", () => ({
  createServerApiClient: jest.fn(),
}));
jest.mock("@/src/lib/api/server/request-headers", () => ({
  readForwardedApiHeaders: jest.fn(),
}));

const client = { role: "request-bound" } as unknown as Client;
const organizationId = "01900000-0000-7000-8000-000000000101";
const teamId = "01900000-0000-7000-8000-000000000201";
const userId = "01900000-0000-7000-8000-000000000301";
const invitationId = "01900000-0000-7000-8000-000000000401";

const teamMember: TeamMemberResponse = {
  id: "01900000-0000-7000-8000-000000000302",
  userId,
  name: "Member User",
  email: "member@example.test",
  imageUrl: null,
  role: "member",
  organizationJoinedAt: "2026-08-01T00:00:00Z",
  teamJoinedAt: "2026-08-01T01:00:00Z",
};

const team: TeamResponse = {
  id: teamId,
  organizationId,
  name: "Platform",
  memberCount: 1,
  members: { items: [teamMember], nextCursor: "members-next" },
  createdAt: "2026-08-01T00:00:00Z",
  updatedAt: "2026-08-01T00:00:00Z",
};

const invitation: InvitationResponse = {
  id: invitationId,
  organizationId,
  organizationName: "Acme",
  canonicalOrganizationKey: "acme",
  teamId,
  teamName: "Platform",
  email: "invitee@example.test",
  role: "member",
  status: "pending",
  displayState: "pending",
  expiresAt: "2026-08-08T00:00:00Z",
  createdAt: "2026-08-01T00:00:00Z",
  inviterId: userId,
  inviterName: "Owner User",
  invitationPath: `/invite/${invitationId}`,
};

const mockedGetCsrf = jest.mocked(getAuthCsrfToken);
const mockedCreateServerClient = jest.mocked(createServerApiClient);
const mockedReadForwardedApiHeaders = jest.mocked(readForwardedApiHeaders);
const reads = {
  accountInvitations: jest.mocked(getAccountInvitations),
  invitationDecision: jest.mocked(getInvitationDecision),
  organizationInvitations: jest.mocked(getOrganizationInvitations),
  teamCandidates: jest.mocked(getTeamMemberCandidates),
  teamMembers: jest.mocked(getTeamMembers),
  teams: jest.mocked(getTeams),
};
const mutations = {
  acceptInvitation: jest.mocked(acceptInvitation),
  addTeamMember: jest.mocked(addTeamMember),
  createInvitation: jest.mocked(createInvitation),
  createTeam: jest.mocked(createTeam),
  deleteTeam: jest.mocked(deleteTeam),
  rejectInvitation: jest.mocked(rejectInvitation),
  removeTeamMember: jest.mocked(removeTeamMember),
  updateTeam: jest.mocked(updateTeam),
};

function sdkSuccess<T>(data: T) {
  return {
    data: { data },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(null, { status: 200 }),
  };
}

beforeEach(() => {
  jest.clearAllMocks();
  mockedReadForwardedApiHeaders.mockResolvedValue({
    cookie: "__Host-template.session=opaque",
    correlationId: "trace-collaboration",
  });
  mockedCreateServerClient.mockReturnValue({ ok: true, client });
});

it("loads every collaboration projection with the generated SDK and the strict SSR header policy", async () => {
  reads.teams.mockResolvedValue(
    sdkSuccess({ items: [team], nextCursor: "teams-next" }),
  );
  reads.teamMembers.mockResolvedValue(
    sdkSuccess({ items: [teamMember], nextCursor: "members-next" }),
  );
  reads.teamCandidates.mockResolvedValue(
    sdkSuccess({
      items: [
        {
          memberId: teamMember.id,
          userId,
          name: "Candidate User",
          email: "candidate@example.test",
          imageUrl: null,
          role: "member" as const,
          joinedAt: "2026-08-01T00:00:00Z",
        },
      ],
      nextCursor: "candidates-next",
    }),
  );
  reads.organizationInvitations.mockResolvedValue(
    sdkSuccess({ items: [invitation], nextCursor: "activity-next" }),
  );
  reads.accountInvitations.mockResolvedValue(
    sdkSuccess({ items: [invitation], nextCursor: "account-next" }),
  );
  reads.invitationDecision.mockResolvedValue(
    sdkSuccess({ invitation, state: "pending" as const, canRespond: true }),
  );

  await expect(
    loadTeams(organizationId, { cursor: "teams-cursor", limit: 10 }),
  ).resolves.toEqual({
    ok: true,
    data: { items: [team], nextCursor: "teams-next" },
  });
  await expect(
    loadTeamMembers(organizationId, teamId, {
      cursor: "members-cursor",
      limit: 20,
    }),
  ).resolves.toEqual({
    ok: true,
    data: { items: [teamMember], nextCursor: "members-next" },
  });
  await expect(
    loadTeamMemberCandidates(organizationId, teamId, {
      q: "candidate",
      cursor: "candidates-cursor",
      limit: 15,
    }),
  ).resolves.toMatchObject({
    ok: true,
    data: { nextCursor: "candidates-next" },
  });
  await expect(
    loadOrganizationInvitations(organizationId, {
      status: "pending",
      cursor: "activity-cursor",
      limit: 25,
    }),
  ).resolves.toEqual({
    ok: true,
    data: { items: [invitation], nextCursor: "activity-next" },
  });
  await expect(
    loadAccountInvitations({ cursor: "account-cursor", limit: 25 }),
  ).resolves.toEqual({
    ok: true,
    data: { items: [invitation], nextCursor: "account-next" },
  });
  await expect(loadInvitationDecision(invitationId)).resolves.toEqual({
    ok: true,
    data: { invitation, state: "pending", canRespond: true },
  });

  expect(mockedReadForwardedApiHeaders).toHaveBeenCalledTimes(6);
  expect(mockedCreateServerClient).toHaveBeenCalledTimes(6);
  expect(mockedCreateServerClient).toHaveBeenCalledWith({
    cookie: "__Host-template.session=opaque",
    correlationId: "trace-collaboration",
  });
  const serverOptions = {
    client,
    cache: "no-store",
    headers: { "X-Template-Session-Renewal": "suppress" },
  };
  expect(reads.teams).toHaveBeenCalledWith({
    ...serverOptions,
    path: { organizationId },
    query: { cursor: "teams-cursor", limit: 10 },
  });
  expect(reads.teamMembers).toHaveBeenCalledWith({
    ...serverOptions,
    path: { organizationId, teamId },
    query: { cursor: "members-cursor", limit: 20 },
  });
  expect(reads.teamCandidates).toHaveBeenCalledWith({
    ...serverOptions,
    path: { organizationId, teamId },
    query: {
      q: "candidate",
      cursor: "candidates-cursor",
      limit: 15,
    },
  });
  expect(reads.organizationInvitations).toHaveBeenCalledWith({
    ...serverOptions,
    path: { organizationId },
    query: { status: "pending", cursor: "activity-cursor", limit: 25 },
  });
  expect(reads.accountInvitations).toHaveBeenCalledWith({
    ...serverOptions,
    query: { cursor: "account-cursor", limit: 25 },
  });
  expect(reads.invitationDecision).toHaveBeenCalledWith({
    ...serverOptions,
    path: { invitationId },
  });
});

it("returns stable safe failures from SSR reads without leaking problem detail", async () => {
  reads.invitationDecision.mockResolvedValue({
    data: undefined,
    error: {
      type: "urn:template:problem:invitation_recipient_mismatch",
      title: "Invitation unavailable",
      status: 403,
      code: "invitation_recipient_mismatch",
      detail: "private recipient address mismatch",
      instance: `/api/v1/invitations/${invitationId}`,
      traceId: "trace-decision",
    },
    request: new Request("https://example.test"),
    response: new Response(null, { status: 403 }),
  });

  const result = await loadInvitationDecision(invitationId);

  expect(result).toEqual({
    ok: false,
    failure: {
      kind: "problem",
      code: "invitation_recipient_mismatch",
      status: 403,
      traceId: "trace-decision",
    },
  });
  expect(JSON.stringify(result)).not.toContain("private recipient");
});

it("returns a configuration failure before any collaboration read", async () => {
  mockedCreateServerClient.mockReturnValue({
    ok: false,
    failure: { kind: "configuration", code: "api_configuration_missing" },
  });

  await expect(loadTeams(organizationId)).resolves.toEqual({
    ok: false,
    failure: { kind: "configuration", code: "api_configuration_missing" },
  });
  expect(reads.teams).not.toHaveBeenCalled();
});

it("gets a fresh CSRF token before each generated collaboration mutation", async () => {
  mockedGetCsrf
    .mockResolvedValueOnce({ ok: true, data: "csrf-create-team" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-update-team" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-delete-team" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-add-member" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-remove-member" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-create-invitation" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-accept" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-reject" });
  mutations.createTeam.mockResolvedValue(sdkSuccess(team));
  mutations.updateTeam.mockResolvedValue(
    sdkSuccess({ ...team, name: "Product" }),
  );
  mutations.deleteTeam.mockResolvedValue(sdkSuccess({ teamId }));
  mutations.addTeamMember.mockResolvedValue(sdkSuccess(teamMember));
  mutations.removeTeamMember.mockResolvedValue(sdkSuccess({ teamId, userId }));
  mutations.createInvitation.mockResolvedValue(sdkSuccess(invitation));
  mutations.acceptInvitation.mockResolvedValue(
    sdkSuccess({
      invitationId,
      organizationId,
      canonicalOrganizationKey: "acme",
    }),
  );
  mutations.rejectInvitation.mockResolvedValue(
    sdkSuccess({
      invitation: {
        ...invitation,
        status: "rejected" as const,
        displayState: "rejected" as const,
      },
      state: "rejected" as const,
      canRespond: false,
    }),
  );

  await expect(
    createBrowserTeam(client, organizationId, { name: "Platform" }),
  ).resolves.toEqual({ ok: true, data: team });
  await expect(
    updateBrowserTeam(client, organizationId, teamId, { name: "Product" }),
  ).resolves.toMatchObject({ ok: true, data: { name: "Product" } });
  await expect(
    deleteBrowserTeam(client, organizationId, teamId),
  ).resolves.toEqual({ ok: true, data: { teamId } });
  await expect(
    addBrowserTeamMember(client, organizationId, teamId, { userId }),
  ).resolves.toEqual({ ok: true, data: teamMember });
  await expect(
    removeBrowserTeamMember(client, organizationId, teamId, userId),
  ).resolves.toEqual({ ok: true, data: { teamId, userId } });
  await expect(
    createBrowserInvitation(client, organizationId, {
      email: invitation.email,
      role: "member",
      teamId,
    }),
  ).resolves.toEqual({ ok: true, data: invitation });
  await expect(
    acceptBrowserInvitation(client, invitationId),
  ).resolves.toMatchObject({ ok: true, data: { invitationId } });
  await expect(
    rejectBrowserInvitation(client, invitationId),
  ).resolves.toMatchObject({ ok: true, data: { state: "rejected" } });

  expect(mockedGetCsrf).toHaveBeenCalledTimes(8);
  expect(mockedGetCsrf).toHaveBeenCalledWith(client);
  expect(mutations.createTeam).toHaveBeenCalledWith({
    client,
    body: { name: "Platform" },
    headers: { "X-CSRF-TOKEN": "csrf-create-team" },
    path: { organizationId },
  });
  expect(mutations.updateTeam).toHaveBeenCalledWith({
    client,
    body: { name: "Product" },
    headers: { "X-CSRF-TOKEN": "csrf-update-team" },
    path: { organizationId, teamId },
  });
  expect(mutations.deleteTeam).toHaveBeenCalledWith({
    client,
    headers: { "X-CSRF-TOKEN": "csrf-delete-team" },
    path: { organizationId, teamId },
  });
  expect(mutations.addTeamMember).toHaveBeenCalledWith({
    client,
    body: { userId },
    headers: { "X-CSRF-TOKEN": "csrf-add-member" },
    path: { organizationId, teamId },
  });
  expect(mutations.removeTeamMember).toHaveBeenCalledWith({
    client,
    headers: { "X-CSRF-TOKEN": "csrf-remove-member" },
    path: { organizationId, teamId, userId },
  });
  expect(mutations.createInvitation).toHaveBeenCalledWith({
    client,
    body: { email: invitation.email, role: "member", teamId },
    headers: { "X-CSRF-TOKEN": "csrf-create-invitation" },
    path: { organizationId },
  });
  expect(mutations.acceptInvitation).toHaveBeenCalledWith({
    client,
    headers: { "X-CSRF-TOKEN": "csrf-accept" },
    path: { invitationId },
  });
  expect(mutations.rejectInvitation).toHaveBeenCalledWith({
    client,
    headers: { "X-CSRF-TOKEN": "csrf-reject" },
    path: { invitationId },
  });
  expect(mockedGetCsrf.mock.invocationCallOrder[0]).toBeLessThan(
    mutations.createTeam.mock.invocationCallOrder[0]!,
  );
});

it("does not invoke a generated mutation when CSRF acquisition fails", async () => {
  mockedGetCsrf.mockResolvedValue({
    ok: false,
    failure: { kind: "problem", code: "antiforgery_failed", status: 400 },
  });

  await expect(acceptBrowserInvitation(client, invitationId)).resolves.toEqual({
    ok: false,
    failure: { kind: "problem", code: "antiforgery_failed", status: 400 },
  });
  expect(mutations.acceptInvitation).not.toHaveBeenCalled();
});

it("normalizes generated mutation failures to stable codes only", async () => {
  mockedGetCsrf.mockResolvedValue({ ok: true, data: "csrf-safe-error" });
  mutations.createInvitation.mockResolvedValue({
    data: undefined,
    error: {
      type: "urn:template:problem:invitation_domain_restricted",
      title: "Invitation domain restricted",
      status: 409,
      code: "invitation_domain_restricted",
      detail: "private allowed-domain configuration",
      instance: `/api/v1/organizations/${organizationId}/invitations`,
      traceId: "trace-create-invitation",
    },
    request: new Request("https://example.test"),
    response: new Response(null, { status: 409 }),
  });

  const result = await createBrowserInvitation(client, organizationId, {
    email: invitation.email,
    role: "member",
  });

  expect(result).toEqual({
    ok: false,
    failure: {
      kind: "problem",
      code: "invitation_domain_restricted",
      status: 409,
      traceId: "trace-create-invitation",
    },
  });
  expect(JSON.stringify(result)).not.toContain("private allowed-domain");
});
