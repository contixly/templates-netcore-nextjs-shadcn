import "server-only";

import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import type { ApplicationShellResult } from "@/src/features/application/application-shell-model";
import { loadAccount } from "@/src/lib/api/account/server/load-account";
import type { OrganizationSummaryResponse } from "@/src/lib/api/generated/types.gen";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";

const unavailable = {
  ok: false,
  failure: { kind: "network", code: "api_unavailable" },
} as const;

function isUserOrganization(
  organization: OrganizationSummaryResponse,
): organization is Extract<
  OrganizationSummaryResponse,
  { accessPrincipal: "user" }
> {
  return organization.accessPrincipal === "user";
}

export async function loadApplicationShell(
  redirectPath: string,
  organizationKey?: string,
): Promise<ApplicationShellResult> {
  const auth = await loadProtectedSession(redirectPath);

  if (!auth.ok) {
    return auth;
  }

  if (
    auth.data.authenticated !== true ||
    !auth.data.session ||
    !auth.data.user
  ) {
    return unavailable;
  }

  const [account, organizations, currentOrganization] = await Promise.all([
    loadAccount(),
    loadOrganizations(),
    organizationKey ? loadOrganization(organizationKey) : null,
  ]);

  if (!account.ok) {
    return account;
  }

  if (!organizations.ok) {
    return organizations;
  }

  if (currentOrganization && !currentOrganization.ok) {
    return currentOrganization;
  }

  return {
    ok: true,
    data: {
      account: account.data,
      organizations: organizations.data.items.filter(isUserOrganization),
      nextOrganizationCursor: organizations.data.nextCursor,
      session: auth.data.session,
      user: auth.data.user,
      currentOrganization: currentOrganization?.data ?? null,
    },
  };
}
