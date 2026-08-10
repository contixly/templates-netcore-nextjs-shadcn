import { getTranslations } from "next-intl/server";

import { Skeleton } from "@/src/components/ui/skeleton";
import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/features/application/ui/settings/settings-shell";

export default async function ProfileLoading() {
  const t = await getTranslations("account.pages.profile");

  return (
    <SettingsPageSection mode="readable">
      <SettingsPageIntro description={t("description")} title={t("title")} />
      <SettingsSection title={t("sectionTitle")}>
        <div aria-busy="true" className="flex flex-col gap-8" role="status">
          <span className="sr-only">{t("loading")}</span>
          <div className="flex items-center gap-6">
            <Skeleton className="size-20 shrink-0 rounded-full" />
            <div className="flex flex-1 flex-col gap-2">
              <Skeleton className="h-4 w-40" />
              <Skeleton className="h-4 w-64 max-w-full" />
            </div>
          </div>
          <div className="flex gap-4">
            <div className="flex flex-1 flex-col gap-2">
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-4 w-40" />
            </div>
            <Skeleton className="h-8 w-24" />
          </div>
          <Skeleton className="h-28 w-full" />
        </div>
      </SettingsSection>
    </SettingsPageSection>
  );
}
