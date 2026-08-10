import { ApplicationNavigationSlot } from "@/src/features/application/ui/application-navigation-slot";
import { accountRoutes } from "@/src/features/account/account-routes";

export default function ConnectionsApplicationNavigation() {
  return <ApplicationNavigationSlot redirectPath={accountRoutes.connections} />;
}
