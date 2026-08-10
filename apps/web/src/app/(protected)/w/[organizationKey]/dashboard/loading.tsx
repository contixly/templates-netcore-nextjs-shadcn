import { getTranslations } from "next-intl/server";

import { DashboardSkeleton } from "@/src/features/dashboard/ui/dashboard-skeleton";

export default async function OrganizationDashboardLoading() {
  const t = await getTranslations("organizations.pages.dashboard");

  return <DashboardSkeleton label={t("loading")} />;
}
