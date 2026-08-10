import { getTranslations } from "next-intl/server";

import { Skeleton } from "@/src/components/ui/skeleton";
import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/features/application/ui/settings/settings-shell";

export default async function DangerLoading() {
  const [page, danger] = await Promise.all([
    getTranslations("account.pages.danger"),
    getTranslations("account.danger"),
  ]);

  return (
    <SettingsPageSection mode="readable">
      <SettingsPageIntro
        description={page("description")}
        title={page("title")}
      />
      <SettingsSection
        description={danger("description")}
        title={danger("title")}
        variant="destructive"
      >
        <div aria-busy="true" className="flex flex-col gap-4" role="status">
          <span className="sr-only">{page("loading")}</span>
          <Skeleton className="h-28 w-full" />
        </div>
      </SettingsSection>
    </SettingsPageSection>
  );
}
