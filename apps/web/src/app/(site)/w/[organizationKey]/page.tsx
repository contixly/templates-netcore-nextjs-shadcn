import { forbidden, redirect } from "next/navigation";
import { connection } from "next/server";

import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { OrganizationOnboarding } from "@/src/components/organizations/organization-onboarding";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";

type WorkspaceRootPageProps = Readonly<{
  params: Promise<{ organizationKey: string }>;
}>;

export default async function WorkspaceRootPage({
  params,
}: WorkspaceRootPageProps) {
  await connection();
  const { organizationKey } = await params;
  const [organization, organizations] = await Promise.all([
    loadOrganization(organizationKey),
    loadOrganizations(),
  ]);

  if (!organizations.ok) {
    return <OrganizationFailure failure={organizations.failure} />;
  }
  if (organizations.data.items.length === 0) {
    return <OrganizationOnboarding />;
  }
  if (organization.ok) {
    redirect(organizationRoutes.dashboard(organization.data.canonicalKey));
  }
  if (
    organization.failure.kind === "problem" &&
    organization.failure.status === 404
  ) {
    forbidden();
  }

  return <OrganizationFailure failure={organization.failure} />;
}
