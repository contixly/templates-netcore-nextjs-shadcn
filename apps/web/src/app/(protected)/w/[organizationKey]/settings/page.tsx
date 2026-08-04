import { forbidden, redirect } from "next/navigation";
import { connection } from "next/server";

import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { OrganizationOnboarding } from "@/src/components/organizations/organization-onboarding";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";
import { buildApplicationPageMetadata } from "@/src/lib/metadata";

export function generateMetadata() {
  return buildApplicationPageMetadata("organizationWorkspace");
}

type OrganizationSettingsRootPageProps = Readonly<{
  params: Promise<{ organizationKey: string }>;
}>;

export default async function OrganizationSettingsRootPage({
  params,
}: OrganizationSettingsRootPageProps) {
  await connection();
  const { organizationKey } = await params;
  const [session, organization] = await Promise.all([
    loadProtectedSession(organizationRoutes.settings(organizationKey)),
    loadOrganization(organizationKey),
  ]);

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
  if (organization.ok) {
    redirect(
      organizationRoutes.settingsWorkspace(organization.data.canonicalKey),
    );
  }
  if (
    organization.failure.kind !== "problem" ||
    organization.failure.status !== 404
  ) {
    return <OrganizationFailure failure={organization.failure} />;
  }

  const organizations = await loadOrganizations();
  if (!organizations.ok) {
    return <OrganizationFailure failure={organizations.failure} />;
  }
  if (organizations.data.items.length === 0) {
    return <OrganizationOnboarding />;
  }
  forbidden();
}
