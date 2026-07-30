import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { OrganizationMemberDirectory } from "@/src/components/organizations/organization-member-directory";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { loadOrganizationMembers } from "@/src/lib/api/organizations/server/load-organization-members";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";

type OrganizationUsersSettingsPageProps = Readonly<{
  params: Promise<{ organizationKey: string }>;
}>;

export default async function OrganizationUsersSettingsPage({
  params,
}: OrganizationUsersSettingsPageProps) {
  await connection();
  const { organizationKey } = await params;
  const sessionPromise = loadProtectedSession(
    organizationRoutes.settingsUsers(organizationKey),
  );
  const organizationPromise = loadOrganization(organizationKey);
  const translationsPromise = getTranslations(
    "organizations.settings.pages.users",
  );
  const [session, organization, t] = await Promise.all([
    sessionPromise,
    organizationPromise,
    translationsPromise,
  ]);

  if (!session.ok) {
    return <OrganizationFailure failure={session.failure} />;
  }
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
  if (!organization.ok) {
    return <OrganizationFailure failure={organization.failure} />;
  }
  if (organizationKey !== organization.data.canonicalKey) {
    redirect(organizationRoutes.settingsUsers(organization.data.canonicalKey));
  }

  const members = await loadOrganizationMembers(organization.data.id);
  if (!members.ok) {
    return <OrganizationFailure failure={members.failure} />;
  }

  return (
    <article className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold">{t("title")}</h1>
        <p className="text-sm text-muted-foreground">{t("description")}</p>
      </header>
      <OrganizationMemberDirectory
        currentUserId={session.data.user.id}
        initialPage={members.data}
        organization={organization.data}
      />
    </article>
  );
}
