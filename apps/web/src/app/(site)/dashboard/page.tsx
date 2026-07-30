import { redirect } from "next/navigation";
import { connection } from "next/server";

import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { authenticationRoutes } from "@/src/features/authentication/authentication-routes";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";

export default async function DashboardPage() {
  await connection();
  const sessionPromise = loadProtectedSession(authenticationRoutes.dashboard);
  const organizationsPromise = loadOrganizations();
  const session = await sessionPromise;

  if (!session.ok) {
    return <OrganizationFailure failure={session.failure} />;
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

  const organizations = await organizationsPromise;
  if (!organizations.ok) {
    return <OrganizationFailure failure={organizations.failure} />;
  }

  const fallback = organizations.data.items[0];
  if (fallback) {
    redirect(organizationRoutes.dashboard(fallback.canonicalKey));
  }

  redirect(organizationRoutes.welcome);
}
