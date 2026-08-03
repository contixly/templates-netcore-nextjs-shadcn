import { ApplicationNavigationSlot } from "@/src/components/application/application-navigation-slot";
import { applicationRoutes } from "@/src/features/application/application-routes";

export default function WelcomeApplicationNavigation() {
  return <ApplicationNavigationSlot redirectPath={applicationRoutes.welcome} />;
}
