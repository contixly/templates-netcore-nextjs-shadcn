import { applicationRoutes } from "@/src/features/application/application-routes";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";

export const dashboardRoutes = {
  application: applicationRoutes.dashboard,
  organization: organizationRoutes.dashboard,
} as const;
