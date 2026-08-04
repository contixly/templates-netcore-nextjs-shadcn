import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/components/application/settings/settings-shell";

import { ApiKeyManagement } from "@/src/components/api-keys/api-key-management";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { loadApiKeys } from "@/src/lib/api/api-keys/server/load-api-keys";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import type { ApiFailure } from "@/src/lib/api/result";
import { buildApplicationPageMetadata } from "@/src/lib/metadata";

type OrganizationApiKeysPageProps = Readonly<{
  params: Promise<{ organizationKey: string }>;
}>;

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
  return buildApplicationPageMetadata("organizationApiKeys");
}

export default async function OrganizationApiKeysPage({
  params,
}: OrganizationApiKeysPageProps) {
  await connection();
  const { organizationKey } = await params;
  const route = organizationRoutes.settingsApiKeys(organizationKey);
  const [session, organization, t] = await Promise.all([
    loadProtectedSession(route),
    loadOrganization(organizationKey),
    getTranslations("apiKeys.page"),
  ]);

  if (!session.ok) {
    return (
      <Failure
        description={t("failureDescription")}
        failure={session.failure}
        title={t("failureTitle")}
      />
    );
  }
  if (
    session.data.authenticated !== true ||
    !session.data.session ||
    !session.data.user
  ) {
    return (
      <Failure
        description={t("failureDescription")}
        failure={{ kind: "network", code: "api_unavailable" }}
        title={t("failureTitle")}
      />
    );
  }
  if (!organization.ok) {
    return (
      <Failure
        description={t("failureDescription")}
        failure={organization.failure}
        title={t("failureTitle")}
      />
    );
  }
  if (organizationKey !== organization.data.canonicalKey) {
    redirect(
      organizationRoutes.settingsApiKeys(organization.data.canonicalKey),
    );
  }

  const owner = {
    kind: "organization",
    organizationId: organization.data.id,
    organizationKey: organization.data.canonicalKey,
    capabilities: {
      canManageApiKeys: organization.data.capabilities.canManageApiKeys,
    },
  } as const;
  const result = await loadApiKeys(owner, { limit: 50 });
  if (!result.ok) {
    return (
      <Failure
        description={t("failureDescription")}
        failure={result.failure}
        title={t("failureTitle")}
      />
    );
  }
  if (!owner.capabilities.canManageApiKeys) {
    return (
      <Failure
        description={t("failureDescription")}
        failure={{
          kind: "problem",
          code: "api_key_permission_denied",
          status: 403,
        }}
        title={t("failureTitle")}
      />
    );
  }

  return (
    <SettingsPageSection mode="wide">
      <SettingsPageIntro
        description={t("organizationDescription")}
        title={t("title")}
      />
      <SettingsSection title={t("organizationSectionTitle")}>
        <ApiKeyManagement
          key={organization.data.id}
          headingLevel={3}
          initialPage={result.data}
          owner={owner}
          showListHeading={false}
        />
      </SettingsSection>
    </SettingsPageSection>
  );
}
