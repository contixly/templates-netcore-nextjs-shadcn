import type { APIRequestContext } from "@playwright/test";

import {
  createOrganization,
  deleteOrganization,
  getOrganizations,
  type OrganizationDetailResponse,
  type OrganizationPageResponse,
  updateOrganization,
} from "../../src/lib/api/generated";
import { clientFor, csrf } from "./generated-auth-api";

function failed(operation: string, status?: number): Error {
  return new Error(
    `Generated organization ${operation} failed with ${status ?? 0}.`,
  );
}

export async function createGeneratedOrganization(
  request: APIRequestContext,
  name: string,
): Promise<OrganizationDetailResponse> {
  const client = clientFor(request);
  const result = await createOrganization({
    client,
    body: { name },
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) {
    throw failed("create", result.response?.status);
  }
  return result.data.data;
}

export async function getGeneratedOrganizations(
  request: APIRequestContext,
): Promise<OrganizationPageResponse> {
  const result = await getOrganizations({
    client: clientFor(request),
    cache: "no-store",
  });
  if (!result.data) {
    throw failed("list", result.response?.status);
  }
  return result.data.data;
}

export async function setGeneratedOrganizationAllowedEmailDomains(
  request: APIRequestContext,
  organizationId: string,
  allowedEmailDomains: readonly string[],
): Promise<OrganizationDetailResponse> {
  const client = clientFor(request);
  const result = await updateOrganization({
    client,
    path: { organizationId },
    body: { allowedEmailDomains: [...allowedEmailDomains] },
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) {
    throw failed("allowed-domain update", result.response?.status);
  }
  return result.data.data;
}

export async function deleteGeneratedOrganization(
  request: APIRequestContext,
  organization: OrganizationDetailResponse,
): Promise<string> {
  const client = clientFor(request);
  const result = await deleteOrganization({
    client,
    path: { organizationId: organization.id },
    body: { confirmationName: organization.name },
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) {
    throw failed("delete", result.response?.status);
  }
  return result.data.data.organizationId;
}
