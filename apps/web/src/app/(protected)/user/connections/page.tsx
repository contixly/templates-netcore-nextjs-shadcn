import { getTranslations } from "next-intl/server";

import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/features/application/ui/settings/settings-shell";

import { ConnectionsList } from "@/src/features/account/ui/connections-list";
import { loadConnections } from "@/src/lib/api/account/server/load-connections";
import type { ApiFailure } from "@/src/lib/api/result";
import { buildApplicationPageMetadata } from "@/src/lib/metadata";

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

export function generateMetadata() {
  return buildApplicationPageMetadata("accountConnections");
}

export default async function ConnectionsPage() {
  const [page, failure, result] = await Promise.all([
    getTranslations("account.pages.connections"),
    getTranslations("account.failure"),
    loadConnections(),
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
    <SettingsPageSection mode="wide">
      <SettingsPageIntro
        description={page("description")}
        title={page("title")}
      />
      <SettingsSection title={page("sectionTitle")}>
        <ConnectionsList headingLevel={3} initialConnections={result.data} />
      </SettingsSection>
    </SettingsPageSection>
  );
}
