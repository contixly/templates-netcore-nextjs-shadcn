import { getTranslations } from "next-intl/server";
import { Suspense } from "react";

import { DashboardRuntime } from "@/src/components/authentication/dashboard-runtime";

export default async function DashboardPage() {
  const t = await getTranslations("auth.dashboard");
  return (
    <Suspense fallback={<p role="status">{t("loading")}</p>}>
      <DashboardRuntime />
    </Suspense>
  );
}
