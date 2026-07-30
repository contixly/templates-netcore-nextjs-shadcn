/** @jest-environment node */

import { getAuthCsrfToken } from "@/src/lib/api/auth/browser/get-auth-csrf";
import {
  addBrowserOrganizationMember,
  createBrowserOrganization,
  deleteBrowserOrganization,
  setActiveBrowserOrganization,
  updateBrowserOrganization,
  updateBrowserOrganizationMemberRole,
} from "@/src/lib/api/organizations/browser/organization-mutations";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizationMembers } from "@/src/lib/api/organizations/server/load-organization-members";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";
import type { Client } from "@/src/lib/api/generated/client";
import {
  addOrganizationMember,
  createOrganization,
  deleteOrganization,
  getOrganizationByKey,
  getOrganizationMembers,
  getOrganizations,
  setActiveOrganization,
  updateOrganization,
  updateOrganizationMemberRole,
} from "@/src/lib/api/generated/sdk.gen";
import type {
  OrganizationDetailResponse,
  OrganizationMemberResponse,
} from "@/src/lib/api/generated/types.gen";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

jest.mock("@/src/lib/api/generated/sdk.gen", () => ({
  addOrganizationMember: jest.fn(),
  createOrganization: jest.fn(),
  deleteOrganization: jest.fn(),
  getOrganizationByKey: jest.fn(),
  getOrganizationMembers: jest.fn(),
  getOrganizations: jest.fn(),
  setActiveOrganization: jest.fn(),
  updateOrganization: jest.fn(),
  updateOrganizationMemberRole: jest.fn(),
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
const mockedCreateServerClient = jest.mocked(createServerApiClient);
const mockedReadForwardedApiHeaders = jest.mocked(readForwardedApiHeaders);
const mockedGetOrganizations = jest.mocked(getOrganizations);
const mockedGetOrganization = jest.mocked(getOrganizationByKey);
const mockedGetMembers = jest.mocked(getOrganizationMembers);
const mockedGetCsrf = jest.mocked(getAuthCsrfToken);
const mockedCreateOrganization = jest.mocked(createOrganization);
const mockedUpdateOrganization = jest.mocked(updateOrganization);
const mockedDeleteOrganization = jest.mocked(deleteOrganization);
const mockedSetActiveOrganization = jest.mocked(setActiveOrganization);
const mockedAddMember = jest.mocked(addOrganizationMember);
const mockedUpdateRole = jest.mocked(updateOrganizationMemberRole);

const organization: OrganizationDetailResponse = {
  id: "01900000-0000-7000-8000-000000000101",
  name: "Acme",
  slug: "acme",
  canonicalKey: "acme",
  createdAt: "2026-07-30T00:00:00Z",
  updatedAt: "2026-07-30T00:00:00Z",
  currentRole: "owner",
  capabilities: {
    canUpdateOrganization: true,
    canDeleteOrganization: true,
    canAddMembers: true,
    canUpdateMemberRoles: true,
  },
  allowedEmailDomains: ["example.test"],
};

const member: OrganizationMemberResponse = {
  id: "01900000-0000-7000-8000-000000000201",
  userId: "01900000-0000-7000-8000-000000000001",
  name: "Member User",
  email: "member@example.test",
  imageUrl: null,
  role: "member",
  joinedAt: "2026-07-30T00:00:00Z",
  emailDomain: "example.test",
  isOutsideAllowedEmailDomains: false,
};

function sdkSuccess<T>(data: T) {
  return {
    data: { data },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  };
}

beforeEach(() => {
  jest.clearAllMocks();
  mockedReadForwardedApiHeaders.mockResolvedValue({
    cookie: "__Host-template.session=opaque",
    correlationId: "trace-organizations",
  });
  mockedCreateServerClient.mockReturnValue({ ok: true, client });
});

it("loads organization projections with only request headers, no-store, and renewal suppression", async () => {
  mockedGetOrganizations.mockResolvedValue(
    sdkSuccess({ items: [organization], nextCursor: "organizations-next" }),
  );
  mockedGetOrganization.mockResolvedValue(sdkSuccess(organization));
  mockedGetMembers.mockResolvedValue(
    sdkSuccess({ items: [member], nextCursor: "members-next" }),
  );

  await expect(
    loadOrganizations({ cursor: "organizations-cursor", limit: 10 }),
  ).resolves.toEqual({
    ok: true,
    data: { items: [organization], nextCursor: "organizations-next" },
  });
  await expect(loadOrganization("acme")).resolves.toEqual({
    ok: true,
    data: organization,
  });
  await expect(
    loadOrganizationMembers(organization.id, {
      cursor: "members-cursor",
      limit: 25,
    }),
  ).resolves.toEqual({
    ok: true,
    data: { items: [member], nextCursor: "members-next" },
  });

  expect(mockedReadForwardedApiHeaders).toHaveBeenCalledTimes(3);
  expect(mockedCreateServerClient).toHaveBeenCalledTimes(3);
  expect(mockedCreateServerClient).toHaveBeenCalledWith({
    cookie: "__Host-template.session=opaque",
    correlationId: "trace-organizations",
  });
  expect(mockedGetOrganizations).toHaveBeenCalledWith({
    client,
    cache: "no-store",
    headers: { "X-Template-Session-Renewal": "suppress" },
    query: { cursor: "organizations-cursor", limit: 10 },
  });
  expect(mockedGetOrganization).toHaveBeenCalledWith({
    client,
    cache: "no-store",
    headers: { "X-Template-Session-Renewal": "suppress" },
    path: { organizationKey: "acme" },
  });
  expect(mockedGetMembers).toHaveBeenCalledWith({
    client,
    cache: "no-store",
    headers: { "X-Template-Session-Renewal": "suppress" },
    path: { organizationId: organization.id },
    query: { cursor: "members-cursor", limit: 25 },
  });
});

it("returns safe normalized server failures", async () => {
  mockedGetOrganization.mockResolvedValue({
    data: undefined,
    error: {
      type: "urn:template:problem:organization_not_found",
      title: "Organization not found",
      status: 404,
      instance: "/api/v1/organizations/by-key/foreign",
      code: "organization_not_found",
      detail: "private organization lookup detail",
      traceId: "trace-safe",
    },
    request: new Request("https://example.test"),
    response: new Response(null, { status: 404 }),
  });

  const result = await loadOrganization("foreign");

  expect(result).toEqual({
    ok: false,
    failure: {
      kind: "problem",
      code: "organization_not_found",
      status: 404,
      traceId: "trace-safe",
    },
  });
  expect(JSON.stringify(result)).not.toContain(
    "private organization lookup detail",
  );
});

it("returns configuration failure without calling organization reads", async () => {
  mockedCreateServerClient.mockReturnValue({
    ok: false,
    failure: {
      kind: "configuration",
      code: "api_configuration_missing",
    },
  });

  await expect(loadOrganizations()).resolves.toEqual({
    ok: false,
    failure: {
      kind: "configuration",
      code: "api_configuration_missing",
    },
  });
  expect(mockedGetOrganizations).not.toHaveBeenCalled();
});

it("gets one fresh CSRF token for each generated organization mutation", async () => {
  mockedGetCsrf
    .mockResolvedValueOnce({ ok: true, data: "csrf-create" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-update" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-delete" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-active" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-add-member" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-update-role" });
  mockedCreateOrganization.mockResolvedValue(sdkSuccess(organization));
  mockedUpdateOrganization.mockResolvedValue(
    sdkSuccess({ ...organization, name: "Updated Acme" }),
  );
  mockedDeleteOrganization.mockResolvedValue(
    sdkSuccess({ organizationId: organization.id }),
  );
  mockedSetActiveOrganization.mockResolvedValue(
    sdkSuccess({ organizationId: organization.id }),
  );
  mockedAddMember.mockResolvedValue(sdkSuccess(member));
  mockedUpdateRole.mockResolvedValue(
    sdkSuccess({ ...member, role: "admin" as const }),
  );

  await expect(
    createBrowserOrganization(client, { name: "Acme" }),
  ).resolves.toEqual({ ok: true, data: organization });
  await expect(
    updateBrowserOrganization(client, organization.id, {
      name: "Updated Acme",
    }),
  ).resolves.toEqual({
    ok: true,
    data: { ...organization, name: "Updated Acme" },
  });
  await expect(
    deleteBrowserOrganization(client, organization.id, {
      confirmationName: "Acme",
    }),
  ).resolves.toEqual({
    ok: true,
    data: { organizationId: organization.id },
  });
  await expect(
    setActiveBrowserOrganization(client, {
      organizationId: organization.id,
    }),
  ).resolves.toEqual({
    ok: true,
    data: { organizationId: organization.id },
  });
  await expect(
    addBrowserOrganizationMember(client, organization.id, {
      userId: member.userId,
      role: "member",
    }),
  ).resolves.toEqual({ ok: true, data: member });
  await expect(
    updateBrowserOrganizationMemberRole(client, organization.id, member.id, {
      role: "admin",
    }),
  ).resolves.toEqual({
    ok: true,
    data: { ...member, role: "admin" },
  });

  expect(mockedGetCsrf).toHaveBeenCalledTimes(6);
  expect(mockedGetCsrf).toHaveBeenCalledWith(client);
  expect(mockedCreateOrganization).toHaveBeenCalledTimes(1);
  expect(mockedCreateOrganization).toHaveBeenCalledWith({
    client,
    body: { name: "Acme" },
    headers: { "X-CSRF-TOKEN": "csrf-create" },
  });
  expect(mockedUpdateOrganization).toHaveBeenCalledTimes(1);
  expect(mockedUpdateOrganization).toHaveBeenCalledWith({
    client,
    body: { name: "Updated Acme" },
    headers: { "X-CSRF-TOKEN": "csrf-update" },
    path: { organizationId: organization.id },
  });
  expect(mockedDeleteOrganization).toHaveBeenCalledTimes(1);
  expect(mockedDeleteOrganization).toHaveBeenCalledWith({
    client,
    body: { confirmationName: "Acme" },
    headers: { "X-CSRF-TOKEN": "csrf-delete" },
    path: { organizationId: organization.id },
  });
  expect(mockedSetActiveOrganization).toHaveBeenCalledTimes(1);
  expect(mockedSetActiveOrganization).toHaveBeenCalledWith({
    client,
    body: { organizationId: organization.id },
    headers: { "X-CSRF-TOKEN": "csrf-active" },
  });
  expect(mockedAddMember).toHaveBeenCalledTimes(1);
  expect(mockedAddMember).toHaveBeenCalledWith({
    client,
    body: { userId: member.userId, role: "member" },
    headers: { "X-CSRF-TOKEN": "csrf-add-member" },
    path: { organizationId: organization.id },
  });
  expect(mockedUpdateRole).toHaveBeenCalledTimes(1);
  expect(mockedUpdateRole).toHaveBeenCalledWith({
    client,
    body: { role: "admin" },
    headers: { "X-CSRF-TOKEN": "csrf-update-role" },
    path: {
      organizationId: organization.id,
      memberId: member.id,
    },
  });
});

it("does not call a generated mutation when CSRF acquisition fails", async () => {
  mockedGetCsrf.mockResolvedValue({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });

  await expect(
    createBrowserOrganization(client, { name: "Acme" }),
  ).resolves.toEqual({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });
  expect(mockedCreateOrganization).not.toHaveBeenCalled();
});

it("preserves only the safe domain-acknowledgement extensions for member confirmation", async () => {
  mockedGetCsrf.mockResolvedValue({ ok: true, data: "csrf-domain-warning" });
  mockedAddMember.mockResolvedValue({
    data: undefined,
    error: {
      type: "urn:template:problem:member_domain_acknowledgement_required",
      title: "Domain acknowledgement required",
      status: 409,
      detail: "private domain policy detail",
      instance: `/api/v1/organizations/${organization.id}/members`,
      code: "member_domain_acknowledgement_required",
      traceId: "trace-domain-warning",
      email: "member@external.test",
      emailDomain: "external.test",
      allowedEmailDomains: ["example.test", "team.example.test"],
      unsafeExtension: "do not expose",
    },
    request: new Request("https://example.test"),
    response: new Response(null, { status: 409 }),
  } as never);

  const result = await addBrowserOrganizationMember(client, organization.id, {
    userId: member.userId,
    role: "member",
  });

  expect(result).toEqual({
    ok: false,
    failure: {
      kind: "problem",
      code: "member_domain_acknowledgement_required",
      status: 409,
      traceId: "trace-domain-warning",
      email: "member@external.test",
      emailDomain: "external.test",
      allowedEmailDomains: ["example.test", "team.example.test"],
    },
  });
  expect(JSON.stringify(result)).not.toContain("private domain policy detail");
  expect(JSON.stringify(result)).not.toContain("unsafeExtension");
});
