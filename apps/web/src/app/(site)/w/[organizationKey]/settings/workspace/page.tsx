import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import { OrganizationDeleteDialog } from "@/src/components/organizations/organization-delete-dialog";
import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { OrganizationSettingsForm } from "@/src/components/organizations/organization-settings-form";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";

type OrganizationWorkspaceSettingsPageProps = Readonly<{
  params: Promise<{ organizationKey: string }>;
}>;

export default async function OrganizationWorkspaceSettingsPage({
  params,
}: OrganizationWorkspaceSettingsPageProps) {
  await connection();
  const { organizationKey } = await params;
  const [session, organization, organizations, t] = await Promise.all([
    loadProtectedSession(organizationRoutes.settingsWorkspace(organizationKey)),
    loadOrganization(organizationKey),
    loadOrganizations(),
    getTranslations("organizations.settings"),
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
    redirect(
      organizationRoutes.settingsWorkspace(organization.data.canonicalKey),
    );
  }

  const hasAnotherAccessibleOrganization =
    organizations.ok &&
    (organizations.data.nextCursor !== null ||
      organizations.data.items.some(
        (candidate) => candidate.id !== organization.data.id,
      ));
  const canDelete =
    organization.data.capabilities.canDeleteOrganization &&
    hasAnotherAccessibleOrganization;

  return (
    <article className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold">{t("pages.workspace.title")}</h1>
        <p className="text-sm text-muted-foreground">
          {t("pages.workspace.description")}
        </p>
      </header>
      <Card>
        <CardHeader>
          <CardTitle>
            <h2>{t("workspace.identityTitle")}</h2>
          </CardTitle>
          <CardDescription>
            {t("workspace.identityDescription")}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <OrganizationSettingsForm initialOrganization={organization.data} />
        </CardContent>
      </Card>
      {canDelete ? (
        <Card className="ring-destructive/40">
          <CardHeader>
            <CardTitle>
              <h2 className="text-destructive">{t("workspace.dangerTitle")}</h2>
            </CardTitle>
            <CardDescription className="text-destructive/80">
              {t("workspace.dangerDescription")}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <OrganizationDeleteDialog
              canDelete
              organization={organization.data}
            />
          </CardContent>
        </Card>
      ) : null}
    </article>
  );
}
