import { getTranslations } from "next-intl/server";

import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/components/application/settings/settings-shell";

import { SessionList } from "@/src/components/account/session-list";
import { loadSessions } from "@/src/lib/api/account/server/load-sessions";
import type { ApiFailure } from "@/src/lib/api/result";

function Failure({
  description,
  failure,
  title,
}: Readonly<{
  description: string;
  failure: ApiFailure;
  title: string;
}>) {
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

export default async function SecurityPage() {
  const [page, failure, result] = await Promise.all([
    getTranslations("account.pages.security"),
    getTranslations("account.failure"),
    loadSessions(),
  ]);

  if (!result.ok) {
    return (
      <Failure
        description={failure("description")}
        failure={result.failure}
        title={failure("title")}
      />
    );
  }

  return (
    <SettingsPageSection mode="readable">
      <SettingsPageIntro
        description={page("description")}
        title={page("title")}
      />
      <SettingsSection title={page("sectionTitle")}>
        <SessionList headingLevel={3} initialPage={result.data} />
      </SettingsSection>
    </SettingsPageSection>
  );
}
