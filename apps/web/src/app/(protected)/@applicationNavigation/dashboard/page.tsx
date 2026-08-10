import { ApplicationNavigationSlot } from "@/src/features/application/ui/application-navigation-slot";
import { applicationRoutes } from "@/src/features/application/application-routes";

export default function DashboardApplicationNavigation() {
  return (
    <ApplicationNavigationSlot redirectPath={applicationRoutes.dashboard} />
  );
}
