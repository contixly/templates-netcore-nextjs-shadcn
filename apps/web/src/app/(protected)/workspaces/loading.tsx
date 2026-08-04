import { getTranslations } from "next-intl/server";

import { Skeleton } from "@/src/components/ui/skeleton";

export default async function WorkspacesLoading() {
  const t = await getTranslations("organizations.pages.workspaces");

  return (
    <div
      aria-busy="true"
      className="mx-auto flex w-full max-w-5xl flex-col gap-8 px-4 py-12"
      role="status"
    >
      <span className="sr-only">{t("loading")}</span>
      <div className="flex flex-col gap-2">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-4 w-full max-w-md" />
      </div>
      <div className="grid gap-4 md:grid-cols-2">
        <Skeleton className="h-48 w-full" />
        <Skeleton className="h-48 w-full" />
      </div>
    </div>
  );
}
