import type { APIRequestContext } from "@playwright/test";

import {
  addOrganizationMember,
  addTeamMember,
  createInvitation,
  createTeam,
  deleteTeam,
  getAccountInvitations,
  getInvitationDecision,
  getOrganizationInvitations,
  getOrganizationMembers,
  getTeamMemberCandidates,
  getTeamMembers,
  getTeams,
  removeTeamMember,
  updateOrganizationMemberRole,
  updateTeam,
  type CreateInvitationRequest,
  type InvitationDecisionResponse,
  type InvitationResponse,
  type OrganizationInvitationPageResponse,
  type OrganizationMemberPageResponse,
  type OrganizationMemberResponse,
  type TeamCandidatePageResponse,
  type TeamDeletionResponse,
  type TeamMemberPageResponse,
  type TeamMemberRemovalResponse,
  type TeamMemberResponse,
  type TeamPageResponse,
  type TeamResponse,
} from "../../src/lib/api/generated";
import { clientFor, csrf } from "./generated-auth-api";

export class GeneratedCollaborationApiError extends Error {
  constructor(
    operation: string,
    readonly status: number | undefined,
    readonly code: string | undefined,
  ) {
    super(
      `Generated collaboration ${operation} failed with ${status ?? 0} (${code ?? "unknown"}).`,
    );
    this.name = "GeneratedCollaborationApiError";
  }
}

function failed(
  operation: string,
  result: Readonly<{
    response?: Response;
    error?: Readonly<{ code?: string }>;
  }>,
): GeneratedCollaborationApiError {
  return new GeneratedCollaborationApiError(
    operation,
    result.response?.status,
    result.error?.code,
  );
}

export async function addGeneratedOrganizationMember(
  request: APIRequestContext,
  organizationId: string,
  userId: string,
  role: "member" | "admin" | "owner",
): Promise<OrganizationMemberResponse> {
  const client = clientFor(request);
  const result = await addOrganizationMember({
    client,
    path: { organizationId },
    body: { userId, role },
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) throw failed("organization-member add", result);
  return result.data.data;
}

export async function updateGeneratedOrganizationMemberRole(
  request: APIRequestContext,
  organizationId: string,
  memberId: string,
  role: "member" | "admin" | "owner",
): Promise<OrganizationMemberResponse> {
  const client = clientFor(request);
  const result = await updateOrganizationMemberRole({
    client,
    path: { organizationId, memberId },
    body: { role },
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) throw failed("organization-member role update", result);
  return result.data.data;
}

export async function createGeneratedTeam(
  request: APIRequestContext,
  organizationId: string,
  name: string,
): Promise<TeamResponse> {
  const client = clientFor(request);
  const result = await createTeam({
    client,
    path: { organizationId },
    body: { name },
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) throw failed("team create", result);
  return result.data.data;
}

export async function updateGeneratedTeam(
  request: APIRequestContext,
  organizationId: string,
  teamId: string,
  name: string,
): Promise<TeamResponse> {
  const client = clientFor(request);
  const result = await updateTeam({
    client,
    path: { organizationId, teamId },
    body: { name },
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) throw failed("team update", result);
  return result.data.data;
}

export async function deleteGeneratedTeam(
  request: APIRequestContext,
  organizationId: string,
  teamId: string,
): Promise<TeamDeletionResponse> {
  const client = clientFor(request);
  const result = await deleteTeam({
    client,
    path: { organizationId, teamId },
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) throw failed("team delete", result);
  return result.data.data;
}

export async function addGeneratedTeamMember(
  request: APIRequestContext,
  organizationId: string,
  teamId: string,
  userId: string,
): Promise<TeamMemberResponse> {
  const client = clientFor(request);
  const result = await addTeamMember({
    client,
    path: { organizationId, teamId },
    body: { userId },
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) throw failed("team-member add", result);
  return result.data.data;
}

export async function removeGeneratedTeamMember(
  request: APIRequestContext,
  organizationId: string,
  teamId: string,
  userId: string,
): Promise<TeamMemberRemovalResponse> {
  const client = clientFor(request);
  const result = await removeTeamMember({
    client,
    path: { organizationId, teamId, userId },
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) throw failed("team-member remove", result);
  return result.data.data;
}

export async function createGeneratedInvitation(
  request: APIRequestContext,
  organizationId: string,
  body: CreateInvitationRequest,
): Promise<InvitationResponse> {
  const client = clientFor(request);
  const result = await createInvitation({
    client,
    path: { organizationId },
    body,
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) throw failed("invitation create", result);
  return result.data.data;
}

export async function getGeneratedTeams(
  request: APIRequestContext,
  organizationId: string,
): Promise<TeamPageResponse> {
  const result = await getTeams({
    client: clientFor(request),
    cache: "no-store",
    path: { organizationId },
    query: { limit: 100 },
  });
  if (!result.data) throw failed("team list", result);
  return result.data.data;
}

export async function getGeneratedTeamMembers(
  request: APIRequestContext,
  organizationId: string,
  teamId: string,
): Promise<TeamMemberPageResponse> {
  const result = await getTeamMembers({
    client: clientFor(request),
    cache: "no-store",
    path: { organizationId, teamId },
    query: { limit: 100 },
  });
  if (!result.data) throw failed("team-member list", result);
  return result.data.data;
}

export async function getGeneratedTeamMemberCandidates(
  request: APIRequestContext,
  organizationId: string,
  teamId: string,
  q?: string,
): Promise<TeamCandidatePageResponse> {
  const result = await getTeamMemberCandidates({
    client: clientFor(request),
    cache: "no-store",
    path: { organizationId, teamId },
    query: { ...(q ? { q } : {}), limit: 100 },
  });
  if (!result.data) throw failed("team candidate list", result);
  return result.data.data;
}

export async function getGeneratedOrganizationInvitations(
  request: APIRequestContext,
  organizationId: string,
): Promise<OrganizationInvitationPageResponse> {
  const result = await getOrganizationInvitations({
    client: clientFor(request),
    cache: "no-store",
    path: { organizationId },
    query: { limit: 100 },
  });
  if (!result.data) throw failed("organization invitation list", result);
  return result.data.data;
}

export async function getGeneratedOrganizationMembers(
  request: APIRequestContext,
  organizationId: string,
): Promise<OrganizationMemberPageResponse> {
  const result = await getOrganizationMembers({
    client: clientFor(request),
    cache: "no-store",
    path: { organizationId },
    query: { limit: 100 },
  });
  if (!result.data) throw failed("organization-member list", result);
  return result.data.data;
}

export async function getGeneratedAccountInvitations(
  request: APIRequestContext,
) {
  const result = await getAccountInvitations({
    client: clientFor(request),
    cache: "no-store",
    query: { limit: 100 },
  });
  if (!result.data) throw failed("account invitation list", result);
  return result.data.data;
}

export async function getGeneratedInvitationDecision(
  request: APIRequestContext,
  invitationId: string,
): Promise<InvitationDecisionResponse> {
  const result = await getInvitationDecision({
    client: clientFor(request),
    cache: "no-store",
    path: { invitationId },
  });
  if (!result.data) throw failed("invitation decision", result);
  return result.data.data;
}
