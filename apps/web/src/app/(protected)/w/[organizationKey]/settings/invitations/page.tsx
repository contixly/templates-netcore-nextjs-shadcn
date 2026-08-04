import { forbidden, redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/components/application/settings/settings-shell";

import { InvitationActivity } from "@/src/components/collaboration/invitation-activity";
import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { collaborationRoutes } from "@/src/features/collaboration/collaboration-routes";
import { loadAllTeams } from "@/src/lib/api/collaboration/server/load-all-teams";
import { loadOrganizationInvitations } from "@/src/lib/api/collaboration/server/load-organization-invitations";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { buildApplicationPageMetadata } from "@/src/lib/metadata";

type InvitationSettingsPageProps = Readonly<{
  params: Promise<{ organizationKey: string }>;
}>;

export function generateMetadata() {
  return buildApplicationPageMetadata("organizationInvitations");
}

export default async function InvitationSettingsPage({
  params,
}: InvitationSettingsPageProps) {
  await connection();
  const { organizationKey } = await params;
  const route = collaborationRoutes.settingsInvitations(organizationKey);
  const [session, organization, t] = await Promise.all([
    loadProtectedSession(route),
    loadOrganization(organizationKey),
    getTranslations("collaboration.invitations.settings"),
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

  // Capability denial intentionally precedes canonicalization so an ordinary
  // member cannot use redirects to discover a private canonical key.
  if (!organization.data.capabilities.canManageInvitations) forbidden();
  if (organizationKey !== organization.data.canonicalKey) {
    redirect(
      collaborationRoutes.settingsInvitations(organization.data.canonicalKey),
    );
  }

  const [invitations, teams] = await Promise.all([
    loadOrganizationInvitations(organization.data.id, { limit: 20 }),
    loadAllTeams(organization.data.id),
  ]);
  if (!invitations.ok)
    return <OrganizationFailure failure={invitations.failure} />;
  if (!teams.ok) return <OrganizationFailure failure={teams.failure} />;

  return (
    <SettingsPageSection mode="wide">
      <SettingsPageIntro description={t("description")} title={t("title")} />
      <SettingsSection title={t("sectionTitle")}>
        <InvitationActivity
          key={organization.data.id}
          initialPage={invitations.data}
          organization={{
            id: organization.data.id,
            currentRole: organization.data.currentRole,
          }}
          teams={teams.data.map((team) => ({
            id: team.id,
            name: team.name,
          }))}
        />
      </SettingsSection>
    </SettingsPageSection>
  );
}
