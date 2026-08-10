import { ApplicationNavigationSlot } from "@/src/features/application/ui/application-navigation-slot";
import { accountRoutes } from "@/src/features/account/account-routes";

export default function ProfileApplicationNavigation() {
  return <ApplicationNavigationSlot redirectPath={accountRoutes.profile} />;
}
