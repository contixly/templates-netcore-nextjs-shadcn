import { getTranslations } from "next-intl/server";

export default async function DashboardLoading() {
  const t = await getTranslations("organizations.pages.dashboard");
  return (
    <div className="mx-auto max-w-3xl px-4 py-12">
      <p role="status">{t("loading")}</p>
    </div>
  );
}
