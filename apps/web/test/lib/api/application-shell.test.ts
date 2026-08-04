/** @jest-environment node */

import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { loadAccount } from "@/src/lib/api/account/server/load-account";
import type {
  AccountResponse,
  OrganizationDetailResponse,
  OrganizationSummaryResponse,
} from "@/src/lib/api/generated/types.gen";
import { loadApplicationShell } from "@/src/lib/api/application/server/load-application-shell";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";

jest.mock("@/src/features/authentication/load-protected-session", () => ({
  loadProtectedSession: jest.fn(),
}));
jest.mock("@/src/lib/api/account/server/load-account", () => ({
  loadAccount: jest.fn(),
}));
jest.mock("@/src/lib/api/organizations/server/load-organization", () => ({
  loadOrganization: jest.fn(),
}));
jest.mock("@/src/lib/api/organizations/server/load-organizations", () => ({
  loadOrganizations: jest.fn(),
}));

const mockLoadProtectedSession = jest.mocked(loadProtectedSession);
const mockLoadAccount = jest.mocked(loadAccount);
const mockLoadOrganization = jest.mocked(loadOrganization);
const mockLoadOrganizations = jest.mocked(loadOrganizations);

const capabilities = {
  canUpdateOrganization: true,
  canDeleteOrganization: true,
  canAddMembers: true,
  canUpdateMemberRoles: true,
  canManageTeams: true,
  canManageInvitations: true,
  canManageApiKeys: true,
};

const account: AccountResponse = {
  id: "account-id",
  displayName: "Account User",
  primaryEmail: "account@example.test",
  imageUrl: null,
  createdAt: "2026-08-03T10:00:00Z",
  verifiedEmails: [],
};

function userOrganization(
  canonicalKey: string,
): Extract<OrganizationSummaryResponse, { accessPrincipal: "user" }> {
  return {
    id: `${canonicalKey}-id`,
    name: canonicalKey,
    slug: canonicalKey,
    canonicalKey,
    createdAt: "2026-08-03T10:00:00Z",
    updatedAt: "2026-08-03T10:00:00Z",
    accessPrincipal: "user",
    currentRole: "owner",
    capabilities,
  };
}

const currentOrganization: OrganizationDetailResponse = {
  ...userOrganization("acme"),
  allowedEmailDomains: [],
};

const organizationPrincipal: OrganizationSummaryResponse = {
  ...userOrganization("service-owned"),
  accessPrincipal: "organization",
  currentRole: "organization",
  capabilities: {
    canUpdateOrganization: false,
    canDeleteOrganization: false,
    canAddMembers: false,
    canUpdateMemberRoles: false,
    canManageTeams: false,
    canManageInvitations: false,
    canManageApiKeys: false,
  },
};

beforeEach(() => {
  jest.clearAllMocks();
  mockLoadProtectedSession.mockResolvedValue({
    ok: true,
    data: {
      authenticated: true,
      user: {
        id: "user-id",
        name: "User",
        email: "user@example.test",
        emailVerified: true,
        image: null,
      },
      session: {
        id: "session-id",
        createdAt: "2026-08-03T10:00:00Z",
        updatedAt: "2026-08-03T10:00:00Z",
        expiresAt: "2026-08-04T10:00:00Z",
        activeOrganizationId: null,
      },
    },
  });
  mockLoadAccount.mockResolvedValue({ ok: true, data: account });
  mockLoadOrganizations.mockResolvedValue({
    ok: true,
    data: {
      items: [userOrganization("first-page"), organizationPrincipal],
      nextCursor: "next-page",
    },
  });
  mockLoadOrganization.mockResolvedValue({
    ok: true,
    data: currentOrganization,
  });
});

it("does not load shell data after an anonymous redirect", async () => {
  mockLoadProtectedSession.mockImplementation(() => {
    throw new Error("redirect:/auth/login?redirect=%2Fuser%2Fsecurity");
  });

  await expect(loadApplicationShell("/user/security")).rejects.toThrow(
    "redirect:/auth/login?redirect=%2Fuser%2Fsecurity",
  );
  expect(mockLoadAccount).not.toHaveBeenCalled();
  expect(mockLoadOrganizations).not.toHaveBeenCalled();
  expect(mockLoadOrganization).not.toHaveBeenCalled();
});

it("loads each authenticated projection once", async () => {
  await expect(
    loadApplicationShell("/w/acme/dashboard", "acme"),
  ).resolves.toMatchObject({
    ok: true,
    data: {
      account,
      currentOrganization: { canonicalKey: "acme" },
      nextOrganizationCursor: "next-page",
      organizations: [{ canonicalKey: "first-page" }],
    },
  });

  expect(mockLoadProtectedSession).toHaveBeenCalledWith("/w/acme/dashboard");
  expect(mockLoadProtectedSession).toHaveBeenCalledTimes(1);
  expect(mockLoadAccount).toHaveBeenCalledTimes(1);
  expect(mockLoadOrganizations).toHaveBeenCalledTimes(1);
  expect(mockLoadOrganization).toHaveBeenCalledWith("acme");
  expect(mockLoadOrganization).toHaveBeenCalledTimes(1);
});

it("does not load a current organization for an account-scoped route", async () => {
  await expect(loadApplicationShell("/user/profile")).resolves.toMatchObject({
    ok: true,
    data: { currentOrganization: null },
  });

  expect(mockLoadOrganization).not.toHaveBeenCalled();
});

it("preserves API failures without treating them as an empty organization list", async () => {
  mockLoadOrganizations.mockResolvedValue({
    ok: false,
    failure: {
      kind: "configuration",
      code: "api_configuration_invalid",
    },
  });

  await expect(loadApplicationShell("/workspaces")).resolves.toEqual({
    ok: false,
    failure: {
      kind: "configuration",
      code: "api_configuration_invalid",
    },
  });
});

it("maps a malformed authenticated success to the safe unavailable failure", async () => {
  mockLoadProtectedSession.mockResolvedValue({
    ok: true,
    data: { authenticated: true, user: null, session: null },
  });

  await expect(loadApplicationShell("/dashboard")).resolves.toEqual({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });
  expect(mockLoadAccount).not.toHaveBeenCalled();
  expect(mockLoadOrganizations).not.toHaveBeenCalled();
});
