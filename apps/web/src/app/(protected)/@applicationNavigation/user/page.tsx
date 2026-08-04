import { ApplicationNavigationSlot } from "@/src/components/application/application-navigation-slot";
import { accountRoutes } from "@/src/features/account/account-routes";

export default function AccountApplicationNavigation() {
  return <ApplicationNavigationSlot redirectPath={accountRoutes.root} />;
}
