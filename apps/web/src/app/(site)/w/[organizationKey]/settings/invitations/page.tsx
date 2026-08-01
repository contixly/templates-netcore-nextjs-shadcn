import { forbidden, redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import { InvitationActivity } from "@/src/components/collaboration/invitation-activity";
import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { collaborationRoutes } from "@/src/features/collaboration/collaboration-routes";
import { loadOrganizationInvitations } from "@/src/lib/api/collaboration/server/load-organization-invitations";
import { loadTeams } from "@/src/lib/api/collaboration/server/load-teams";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";

type InvitationSettingsPageProps = Readonly<{
  params: Promise<{ organizationKey: string }>;
}>;

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
    loadTeams(organization.data.id, { limit: 100 }),
  ]);
  if (!invitations.ok)
    return <OrganizationFailure failure={invitations.failure} />;
  if (!teams.ok) return <OrganizationFailure failure={teams.failure} />;

  return (
    <article className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold">{t("title")}</h1>
        <p className="text-sm text-muted-foreground">{t("description")}</p>
      </header>
      <InvitationActivity
        key={organization.data.id}
        initialPage={invitations.data}
        organization={{
          id: organization.data.id,
          currentRole: organization.data.currentRole,
        }}
        teams={teams.data.items.map((team) => ({
          id: team.id,
          name: team.name,
        }))}
      />
    </article>
  );
}
