import { getTranslations } from "next-intl/server";
import { IconAlertTriangle } from "@tabler/icons-react";

import { DeleteAccountDialog } from "@/src/features/account/ui/delete-account-dialog";
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
  const [page, danger, deleteAccount, failure, result] = await Promise.all([
    getTranslations("account.pages.danger"),
    getTranslations("account.danger"),
    getTranslations("account.deleteAccount"),
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
        title={
          <span className="flex items-center gap-2">
            <IconAlertTriangle
              aria-hidden="true"
              className="size-5 text-destructive"
            />
            {danger("title")}
          </span>
        }
        variant="destructive"
      >
        <div className="rounded-lg border border-destructive/30 bg-destructive/5 p-4">
          <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
            <div className="flex flex-col gap-1">
              <h3 className="text-sm font-medium">{deleteAccount("title")}</h3>
              <p className="text-sm text-muted-foreground">
                {deleteAccount("description")}
              </p>
            </div>
            <div className="shrink-0">
              <DeleteAccountDialog primaryEmail={result.data.primaryEmail} />
            </div>
          </div>
        </div>
      </SettingsSection>
    </SettingsPageSection>
  );
}
