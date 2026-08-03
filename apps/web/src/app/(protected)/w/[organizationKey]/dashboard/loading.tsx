import { getTranslations } from "next-intl/server";

import { DashboardSkeleton } from "@/src/components/dashboard/dashboard-skeleton";

export default async function OrganizationDashboardLoading() {
  const t = await getTranslations("organizations.pages.dashboard");

  return <DashboardSkeleton label={t("loading")} />;
}
