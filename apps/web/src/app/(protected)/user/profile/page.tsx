import { getTranslations } from "next-intl/server";

import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/components/application/settings/settings-shell";

import { ProfileForm } from "@/src/components/account/profile-form";
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
  return buildApplicationPageMetadata("accountProfile");
}

export default async function ProfilePage() {
  const [page, failure, result] = await Promise.all([
    getTranslations("account.pages.profile"),
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
      <SettingsSection title={page("sectionTitle")}>
        <ProfileForm headingLevel={3} initialAccount={result.data} />
      </SettingsSection>
    </SettingsPageSection>
  );
}
