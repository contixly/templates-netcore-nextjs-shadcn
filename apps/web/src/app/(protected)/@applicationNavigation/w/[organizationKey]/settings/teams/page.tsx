import { ApplicationNavigationSlot } from "@/src/features/application/ui/application-navigation-slot";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";

export default async function OrganizationTeamsApplicationNavigation({
  params,
}: Readonly<{ params: Promise<{ organizationKey: string }> }>) {
  const { organizationKey } = await params;

  return (
    <ApplicationNavigationSlot
      redirectPath={organizationRoutes.settingsTeams(organizationKey)}
      organizationKey={organizationKey}
    />
  );
}
