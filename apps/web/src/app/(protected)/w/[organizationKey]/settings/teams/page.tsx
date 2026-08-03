import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import { TeamDirectory } from "@/src/components/collaboration/team-directory";
import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { collaborationRoutes } from "@/src/features/collaboration/collaboration-routes";
import { loadTeams } from "@/src/lib/api/collaboration/server/load-teams";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";

type TeamSettingsPageProps = Readonly<{
  params: Promise<{ organizationKey: string }>;
}>;

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
    <article className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold">{t("title")}</h1>
        <p className="text-sm text-muted-foreground">{t("description")}</p>
      </header>
      <TeamDirectory
        key={organization.data.id}
        initialPage={teams.data}
        organization={{
          id: organization.data.id,
          canManageTeams: organization.data.capabilities.canManageTeams,
        }}
      />
    </article>
  );
}
