import { getTranslations } from "next-intl/server";

import { Skeleton } from "@/src/components/ui/skeleton";

export default async function OrganizationSettingsLoading() {
  const t = await getTranslations("organizations.settings.navigation");
  return (
    <div
      aria-busy="true"
      className="flex min-w-0 flex-1 flex-col gap-8 px-4 py-8 md:px-6"
      role="status"
    >
      <span className="sr-only">{t("loading")}</span>
      <div className="flex flex-col gap-2">
        <Skeleton className="h-7 w-48" />
        <Skeleton className="h-4 w-full max-w-md" />
      </div>
      <Skeleton className="h-56 w-full" />
    </div>
  );
}
