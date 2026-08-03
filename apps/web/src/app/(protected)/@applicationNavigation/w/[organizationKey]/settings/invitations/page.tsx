import { ApplicationNavigationSlot } from "@/src/components/application/application-navigation-slot";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";

export default async function OrganizationInvitationsApplicationNavigation({
  params,
}: Readonly<{ params: Promise<{ organizationKey: string }> }>) {
  const { organizationKey } = await params;

  return (
    <ApplicationNavigationSlot
      redirectPath={organizationRoutes.settingsInvitations(organizationKey)}
      organizationKey={organizationKey}
    />
  );
}
