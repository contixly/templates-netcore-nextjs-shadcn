import { getTranslations } from "next-intl/server";

import {
  SettingsPageIntro,
  SettingsPageSection,
} from "@/src/features/application/ui/settings/settings-shell";

import { ApiKeyManagement } from "@/src/features/api-keys/ui/api-key-management";
import { loadApiKeys } from "@/src/lib/api/api-keys/server/load-api-keys";
import type { ApiFailure } from "@/src/lib/api/result";
import { buildApplicationPageMetadata } from "@/src/lib/metadata";

function Failure({
  description,
  failure,
  title,
}: Readonly<{ description: string; failure: ApiFailure; title: string }>) {
  return (
    <section className="flex flex-col gap-2" role="alert">
      <h1 className="text-xl font-semibold">{title}</h1>
      <p className="text-sm text-muted-foreground">{description}</p>
      {failure.kind === "problem" && failure.traceId ? (
        <p className="font-mono text-xs text-muted-foreground">
          {failure.traceId}
        </p>
      ) : null}
    </section>
  );
}

export function generateMetadata() {
  return buildApplicationPageMetadata("accountApiKeys");
}

export default async function PersonalApiKeysPage() {
  const [t, result] = await Promise.all([
    getTranslations("apiKeys.page"),
    loadApiKeys({ kind: "personal" }, { limit: 50 }),
  ]);

  if (!result.ok)
    return (
      <Failure
        description={t("failureDescription")}
        failure={result.failure}
        title={t("failureTitle")}
      />
    );

  return (
    <SettingsPageSection mode="wide">
      <SettingsPageIntro description={t("description")} title={t("title")} />
      <ApiKeyManagement
        initialPage={result.data}
        owner={{ kind: "personal" }}
      />
    </SettingsPageSection>
  );
}
