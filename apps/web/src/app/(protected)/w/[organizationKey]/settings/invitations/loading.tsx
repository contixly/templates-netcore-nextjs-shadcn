import { getTranslations } from "next-intl/server";

import { Skeleton } from "@/src/components/ui/skeleton";

export default async function InvitationSettingsLoading() {
  const t = await getTranslations("collaboration.invitations.settings");
  return (
    <div aria-busy="true" className="flex flex-col gap-8" role="status">
      <span className="sr-only">{t("loading")}</span>
      <div className="flex flex-col gap-2">
        <Skeleton className="h-7 w-56" />
        <Skeleton className="h-4 w-full max-w-md" />
      </div>
      <Skeleton className="h-56 w-full" />
    </div>
  );
}
