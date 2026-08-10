import { getTranslations } from "next-intl/server";

import { DeleteAccountDialog } from "@/src/components/account/delete-account-dialog";
import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/features/application/ui/settings/settings-shell";
import { loadAccount } from "@/src/lib/api/account/server/load-account";
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
  return buildApplicationPageMetadata("accountDanger");
}

export default async function DangerPage() {
  const [page, danger, failure, result] = await Promise.all([
    getTranslations("account.pages.danger"),
    getTranslations("account.danger"),
    getTranslations("account.failure"),
    loadAccount(),
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
      <SettingsSection
        description={danger("description")}
        title={danger("title")}
        variant="destructive"
      >
        <p className="text-sm font-medium text-destructive">
          {danger("warning")}
        </p>
        <DeleteAccountDialog primaryEmail={result.data.primaryEmail} />
      </SettingsSection>
    </SettingsPageSection>
  );
}
