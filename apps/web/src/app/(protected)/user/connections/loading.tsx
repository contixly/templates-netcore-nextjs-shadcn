import { getTranslations } from "next-intl/server";

import { Skeleton } from "@/src/components/ui/skeleton";
import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/features/application/ui/settings/settings-shell";

export default async function ConnectionsLoading() {
  const t = await getTranslations("account.pages.connections");

  return (
    <SettingsPageSection mode="readable">
      <SettingsPageIntro description={t("description")} title={t("title")} />
      <SettingsSection title={t("sectionTitle")}>
        <div aria-busy="true" className="flex flex-col gap-4" role="status">
          <span className="sr-only">{t("loading")}</span>
          {[0, 1, 2].map((item) => (
            <div className="flex items-center gap-4" key={item}>
              <Skeleton className="size-10 shrink-0 rounded-full" />
              <div className="flex flex-1 flex-col gap-2">
                <Skeleton className="h-4 w-28" />
                <Skeleton className="h-4 w-44 max-w-full" />
              </div>
              <Skeleton className="h-8 w-32" />
            </div>
          ))}
        </div>
      </SettingsSection>
    </SettingsPageSection>
  );
}
