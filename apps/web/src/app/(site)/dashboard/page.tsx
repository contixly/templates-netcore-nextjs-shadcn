import { redirect } from "next/navigation";
import { connection } from "next/server";

import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { authenticationRoutes } from "@/src/features/authentication/authentication-routes";
import { authLoginUrl } from "@/src/features/authentication/sanitize-auth-redirect";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";

export default async function DashboardPage() {
  await connection();
  const [session, organizations] = await Promise.all([
    loadServerAuthSession(),
    loadOrganizations(),
  ]);

  if (!session.ok) {
    return <OrganizationFailure failure={session.failure} />;
  }
  if (session.data.authenticated === false) {
    redirect(authLoginUrl(authenticationRoutes.dashboard));
  }
  if (
    session.data.authenticated !== true ||
    !session.data.session ||
    !session.data.user
  ) {
    return (
      <OrganizationFailure
        failure={{ kind: "network", code: "api_unavailable" }}
      />
    );
  }
  if (!organizations.ok) {
    return <OrganizationFailure failure={organizations.failure} />;
  }

  const activeOrganizationId =
    session.data.session.activeOrganizationId ?? undefined;
  if (activeOrganizationId) {
    const activeOrganization = await loadOrganization(activeOrganizationId);
    if (activeOrganization.ok) {
      redirect(
        organizationRoutes.dashboard(activeOrganization.data.canonicalKey),
      );
    }
    if (
      activeOrganization.failure.kind !== "problem" ||
      activeOrganization.failure.status !== 404
    ) {
      return <OrganizationFailure failure={activeOrganization.failure} />;
    }
  }

  const fallback = organizations.data.items[0];
  if (fallback) {
    redirect(organizationRoutes.dashboard(fallback.canonicalKey));
  }

  redirect(organizationRoutes.welcome);
}
