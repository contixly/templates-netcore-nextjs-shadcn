import { ApplicationNavigationSlot } from "@/src/features/application/ui/application-navigation-slot";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";

export default async function OrganizationSettingsApplicationNavigation({
  params,
}: Readonly<{ params: Promise<{ organizationKey: string }> }>) {
  const { organizationKey } = await params;

  return (
    <ApplicationNavigationSlot
      redirectPath={organizationRoutes.settings(organizationKey)}
      organizationKey={organizationKey}
    />
  );
}
