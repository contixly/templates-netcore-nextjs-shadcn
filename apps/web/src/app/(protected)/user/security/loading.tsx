import { getTranslations } from "next-intl/server";

import { Skeleton } from "@/src/components/ui/skeleton";
import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/features/application/ui/settings/settings-shell";

export default async function SecurityLoading() {
  const t = await getTranslations("account.pages.security");

  return (
    <SettingsPageSection mode="readable">
      <SettingsPageIntro description={t("description")} title={t("title")} />
      <SettingsSection title={t("sectionTitle")}>
        <div
          aria-busy="true"
          className="flex min-h-80 flex-col gap-4"
          role="status"
        >
          <span className="sr-only">{t("loading")}</span>
          {[0, 1].map((item) => (
            <div className="flex items-center gap-4" key={item}>
              <Skeleton className="size-10 shrink-0 rounded-full" />
              <div className="flex flex-1 flex-col gap-2">
                <Skeleton className="h-4 w-32" />
                <Skeleton className="h-4 w-56 max-w-full" />
                <Skeleton className="h-3 w-44 max-w-full" />
              </div>
            </div>
          ))}
        </div>
      </SettingsSection>
    </SettingsPageSection>
  );
}
