import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/components/application/settings/settings-shell";

import { TeamDirectory } from "@/src/components/collaboration/team-directory";
import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { collaborationRoutes } from "@/src/features/collaboration/collaboration-routes";
import { loadTeams } from "@/src/lib/api/collaboration/server/load-teams";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { buildApplicationPageMetadata } from "@/src/lib/metadata";

type TeamSettingsPageProps = Readonly<{
  params: Promise<{ organizationKey: string }>;
}>;

export function generateMetadata() {
  return buildApplicationPageMetadata("organizationTeams");
}

export default async function TeamSettingsPage({
  params,
}: TeamSettingsPageProps) {
  await connection();
  const { organizationKey } = await params;
  const route = collaborationRoutes.settingsTeams(organizationKey);
  const [session, organization, t] = await Promise.all([
    loadProtectedSession(route),
    loadOrganization(organizationKey),
    getTranslations("collaboration.teams.page"),
  ]);

  if (!session.ok) return <OrganizationFailure failure={session.failure} />;
  if (
    session.data.authenticated !== true ||
    !session.data.session ||
    !session.data.user
  ) {
    return (
      <OrganizationFailure
        failure={{ kind: "network", code: "api_unavailable" }}
      />
    );
  }
  if (!organization.ok)
    return <OrganizationFailure failure={organization.failure} />;
  if (organizationKey !== organization.data.canonicalKey) {
    redirect(collaborationRoutes.settingsTeams(organization.data.canonicalKey));
  }

  const teams = await loadTeams(organization.data.id, { limit: 20 });
  if (!teams.ok) return <OrganizationFailure failure={teams.failure} />;

  return (
    <SettingsPageSection mode="wide">
      <SettingsPageIntro description={t("description")} title={t("title")} />
      <SettingsSection title={t("sectionTitle")}>
        <TeamDirectory
          key={organization.data.id}
          initialPage={teams.data}
          organization={{
            id: organization.data.id,
            canManageTeams: organization.data.capabilities.canManageTeams,
          }}
          showListHeading={false}
        />
      </SettingsSection>
    </SettingsPageSection>
  );
}
