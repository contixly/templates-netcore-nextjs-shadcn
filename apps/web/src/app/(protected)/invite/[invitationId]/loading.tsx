import { getTranslations } from "next-intl/server";

import { Skeleton } from "@/src/components/ui/skeleton";

export default async function InvitationDecisionLoading() {
  const t = await getTranslations("collaboration.decision.page");
  return (
    <div
      aria-busy="true"
      className="mx-auto flex w-full max-w-2xl flex-1 flex-col gap-4 px-4 py-12"
      role="status"
    >
      <span className="sr-only">{t("loading")}</span>
      <Skeleton className="h-7 w-48" />
      <Skeleton className="h-64 w-full" />
    </div>
  );
}
