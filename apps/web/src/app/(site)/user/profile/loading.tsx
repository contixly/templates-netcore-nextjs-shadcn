import { getTranslations } from "next-intl/server";

import { Skeleton } from "@/src/components/ui/skeleton";

export default async function ProfileLoading() {
  const t = await getTranslations("account.pages.profile");

  return (
    <div aria-busy="true" className="flex flex-col gap-8" role="status">
      <span className="sr-only">{t("loading")}</span>
      <div className="flex flex-col gap-2">
        <Skeleton className="h-7 w-48" />
        <Skeleton className="h-4 w-full max-w-md" />
      </div>
      <Skeleton className="h-20 w-20 rounded-full" />
      <Skeleton className="h-24 w-full" />
      <Skeleton className="h-36 w-full" />
    </div>
  );
}
