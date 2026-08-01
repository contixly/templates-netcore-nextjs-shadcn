"use client";

import { runCsrfMutation } from "@/src/lib/api/browser/run-csrf-mutation";
import {
  acceptInvitation,
  addTeamMember,
  createInvitation,
  createTeam,
  deleteTeam,
  rejectInvitation,
  removeTeamMember,
  updateTeam,
} from "@/src/lib/api/generated/sdk.gen";
import type {
  AcceptedInvitationResponse,
  AddTeamMemberRequest,
  CreateInvitationRequest,
  InvitationDecisionResponse,
  InvitationResponse,
  TeamDeletionResponse,
  TeamMemberRemovalResponse,
  TeamMemberResponse,
  TeamNameRequest,
  TeamResponse,
} from "@/src/lib/api/generated/types.gen";
import type { Client } from "@/src/lib/api/generated/client";
import type { ApiResult } from "@/src/lib/api/result";

export function createBrowserTeam(
  client: Client,
  organizationId: string,
  body: TeamNameRequest,
): Promise<ApiResult<TeamResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    createTeam({
      client,
      body,
      headers: { "X-CSRF-TOKEN": csrfToken },
      path: { organizationId },
    }),
  );
}

export function updateBrowserTeam(
  client: Client,
  organizationId: string,
  teamId: string,
  body: TeamNameRequest,
): Promise<ApiResult<TeamResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    updateTeam({
      client,
      body,
      headers: { "X-CSRF-TOKEN": csrfToken },
      path: { organizationId, teamId },
    }),
  );
}

export function deleteBrowserTeam(
  client: Client,
  organizationId: string,
  teamId: string,
): Promise<ApiResult<TeamDeletionResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    deleteTeam({
      client,
      headers: { "X-CSRF-TOKEN": csrfToken },
      path: { organizationId, teamId },
    }),
  );
}

export function addBrowserTeamMember(
  client: Client,
  organizationId: string,
  teamId: string,
  body: AddTeamMemberRequest,
): Promise<ApiResult<TeamMemberResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    addTeamMember({
      client,
      body,
      headers: { "X-CSRF-TOKEN": csrfToken },
      path: { organizationId, teamId },
    }),
  );
}

export function removeBrowserTeamMember(
  client: Client,
  organizationId: string,
  teamId: string,
  userId: string,
): Promise<ApiResult<TeamMemberRemovalResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    removeTeamMember({
      client,
      headers: { "X-CSRF-TOKEN": csrfToken },
      path: { organizationId, teamId, userId },
    }),
  );
}

export function createBrowserInvitation(
  client: Client,
  organizationId: string,
  body: CreateInvitationRequest,
): Promise<ApiResult<InvitationResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    createInvitation({
      client,
      body,
      headers: { "X-CSRF-TOKEN": csrfToken },
      path: { organizationId },
    }),
  );
}

export function acceptBrowserInvitation(
  client: Client,
  invitationId: string,
): Promise<ApiResult<AcceptedInvitationResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    acceptInvitation({
      client,
      headers: { "X-CSRF-TOKEN": csrfToken },
      path: { invitationId },
    }),
  );
}

export function rejectBrowserInvitation(
  client: Client,
  invitationId: string,
): Promise<ApiResult<InvitationDecisionResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    rejectInvitation({
      client,
      headers: { "X-CSRF-TOKEN": csrfToken },
      path: { invitationId },
    }),
  );
}
