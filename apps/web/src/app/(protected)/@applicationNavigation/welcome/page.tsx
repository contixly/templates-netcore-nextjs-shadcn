import { ApplicationNavigationSlot } from "@/src/features/application/ui/application-navigation-slot";
import { applicationRoutes } from "@/src/features/application/application-routes";

export default function WelcomeApplicationNavigation() {
  return <ApplicationNavigationSlot redirectPath={applicationRoutes.welcome} />;
}
