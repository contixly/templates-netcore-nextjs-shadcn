import { ApplicationNavigationSlot } from "@/src/features/application/ui/application-navigation-slot";
import { dashboardRoutes } from "@/src/features/dashboard/dashboard-routes";

export default async function OrganizationDashboardApplicationNavigation({
  params,
}: Readonly<{ params: Promise<{ organizationKey: string }> }>) {
  const { organizationKey } = await params;

  return (
    <ApplicationNavigationSlot
      redirectPath={dashboardRoutes.organization(organizationKey)}
      organizationKey={organizationKey}
    />
  );
}
