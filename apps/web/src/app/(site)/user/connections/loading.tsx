import { getTranslations } from "next-intl/server";

import { Skeleton } from "@/src/components/ui/skeleton";

export default async function ConnectionsLoading() {
  const t = await getTranslations("account.pages.connections");

  return (
    <div aria-busy="true" className="flex flex-col gap-8" role="status">
      <span className="sr-only">{t("loading")}</span>
      <div className="flex flex-col gap-2">
        <Skeleton className="h-7 w-48" />
        <Skeleton className="h-4 w-full max-w-md" />
      </div>
      <Skeleton className="h-28 w-full" />
      <Skeleton className="h-28 w-full" />
      <Skeleton className="h-28 w-full" />
    </div>
  );
}
