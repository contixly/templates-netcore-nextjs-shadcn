"use client";

import { runCsrfMutation } from "@/src/lib/api/browser/run-csrf-mutation";
import {
  addOrganizationMember,
  createOrganization,
  deleteOrganization,
  setActiveOrganization,
  updateOrganization,
  updateOrganizationMemberRole,
} from "@/src/lib/api/generated/sdk.gen";
import type {
  ActiveOrganizationResponse,
  AddOrganizationMemberRequest,
  CreateOrganizationRequest,
  DeleteOrganizationRequest,
  OrganizationDeletionResponse,
  OrganizationDetailResponse,
  OrganizationMemberResponse,
  SetActiveOrganizationRequest,
  UpdateOrganizationMemberRoleRequest,
  UpdateOrganizationRequest,
} from "@/src/lib/api/generated/types.gen";
import type { Client } from "@/src/lib/api/generated/client";
import type { ApiResult } from "@/src/lib/api/result";

export function createBrowserOrganization(
  client: Client,
  body: CreateOrganizationRequest,
): Promise<ApiResult<OrganizationDetailResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    createOrganization({
      client,
      body,
      headers: { "X-CSRF-TOKEN": csrfToken },
    }),
  );
}

export function updateBrowserOrganization(
  client: Client,
  organizationId: string,
  body: UpdateOrganizationRequest,
): Promise<ApiResult<OrganizationDetailResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    updateOrganization({
      client,
      body,
      headers: { "X-CSRF-TOKEN": csrfToken },
      path: { organizationId },
    }),
  );
}

export function deleteBrowserOrganization(
  client: Client,
  organizationId: string,
  body: DeleteOrganizationRequest,
): Promise<ApiResult<OrganizationDeletionResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    deleteOrganization({
      client,
      body,
      headers: { "X-CSRF-TOKEN": csrfToken },
      path: { organizationId },
    }),
  );
}

export function setActiveBrowserOrganization(
  client: Client,
  body: SetActiveOrganizationRequest,
): Promise<ApiResult<ActiveOrganizationResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    setActiveOrganization({
      client,
      body,
      headers: { "X-CSRF-TOKEN": csrfToken },
    }),
  );
}

export function addBrowserOrganizationMember(
  client: Client,
  organizationId: string,
  body: AddOrganizationMemberRequest,
): Promise<ApiResult<OrganizationMemberResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    addOrganizationMember({
      client,
      body,
      headers: { "X-CSRF-TOKEN": csrfToken },
      path: { organizationId },
    }),
  );
}

export function updateBrowserOrganizationMemberRole(
  client: Client,
  organizationId: string,
  memberId: string,
  body: UpdateOrganizationMemberRoleRequest,
): Promise<ApiResult<OrganizationMemberResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    updateOrganizationMemberRole({
      client,
      body,
      headers: { "X-CSRF-TOKEN": csrfToken },
      path: { organizationId, memberId },
    }),
  );
}
