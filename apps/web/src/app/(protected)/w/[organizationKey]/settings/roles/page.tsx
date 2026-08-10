import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/features/application/ui/settings/settings-shell";

import { OrganizationFailure } from "@/src/features/organizations/ui/organization-list";
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
import { buildApplicationPageMetadata } from "@/src/lib/metadata";

type OrganizationRolesSettingsPageProps = Readonly<{
  params: Promise<{ organizationKey: string }>;
}>;

export function generateMetadata() {
  return buildApplicationPageMetadata("organizationRoles");
}

export default async function OrganizationRolesSettingsPage({
  params,
}: OrganizationRolesSettingsPageProps) {
  await connection();
  const { organizationKey } = await params;
  const [session, organization, t] = await Promise.all([
    loadProtectedSession(organizationRoutes.settingsRoles(organizationKey)),
    loadOrganization(organizationKey),
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
    redirect(organizationRoutes.settingsRoles(organization.data.canonicalKey));
  }

  const fixedRoles = ["owner", "admin", "member"] as const;
  return (
    <SettingsPageSection mode="readable">
      <SettingsPageIntro
        description={t("pages.roles.description")}
        title={t("pages.roles.title")}
      />
      <SettingsSection title={t("pages.roles.sectionTitle")}>
        <div className="grid gap-4">
          {fixedRoles.map((role) => (
            <Card key={role}>
              <CardHeader>
                <CardTitle>
                  <h3>{t(`roles.${role}.title`)}</h3>
                </CardTitle>
                <CardDescription>{t(`roles.${role}.summary`)}</CardDescription>
              </CardHeader>
              <CardContent>
                <p className="text-sm text-muted-foreground">
                  {t(`roles.${role}.description`)}
                </p>
              </CardContent>
            </Card>
          ))}
        </div>
        <p className="text-sm text-muted-foreground">
          {t("roles.fixedNotice")}
        </p>
      </SettingsSection>
    </SettingsPageSection>
  );
}
