import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/components/application/settings/settings-shell";

import { OrganizationDeleteDialog } from "@/src/components/organizations/organization-delete-dialog";
import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { OrganizationSettingsForm } from "@/src/components/organizations/organization-settings-form";
import { Card, CardContent } from "@/src/components/ui/card";
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
    <SettingsPageSection mode="readable">
      <SettingsPageIntro
        description={t("pages.workspace.description")}
        title={t("pages.workspace.title")}
      />
      <SettingsSection
        description={t("workspace.identityDescription")}
        title={t("workspace.identityTitle")}
      >
        <Card>
          <CardContent>
            <OrganizationSettingsForm
              key={organization.data.id}
              initialOrganization={{
                id: organization.data.id,
                name: organization.data.name,
                slug: organization.data.slug,
                canonicalKey: organization.data.canonicalKey,
                allowedEmailDomains: organization.data.allowedEmailDomains,
                capabilities: {
                  canUpdateOrganization:
                    organization.data.capabilities.canUpdateOrganization,
                },
              }}
            />
          </CardContent>
        </Card>
      </SettingsSection>
      {canDelete ? (
        <SettingsSection
          description={t("workspace.dangerDescription")}
          title={t("workspace.dangerTitle")}
          variant="destructive"
        >
          <Card className="ring-destructive/40">
            <CardContent>
              <OrganizationDeleteDialog
                key={organization.data.id}
                canDelete
                organization={{
                  id: organization.data.id,
                  name: organization.data.name,
                }}
              />
            </CardContent>
          </Card>
        </SettingsSection>
      ) : null}
    </SettingsPageSection>
  );
}
