import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import { OrganizationFailure } from "@/src/components/organizations/organization-list";
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

type OrganizationRolesSettingsPageProps = Readonly<{
  params: Promise<{ organizationKey: string }>;
}>;

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
    <article className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold">{t("pages.roles.title")}</h1>
        <p className="text-sm text-muted-foreground">
          {t("pages.roles.description")}
        </p>
      </header>
      <div className="grid gap-4">
        {fixedRoles.map((role) => (
          <Card key={role}>
            <CardHeader>
              <CardTitle>
                <h2>{t(`roles.${role}.title`)}</h2>
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
      <p className="text-sm text-muted-foreground">{t("roles.fixedNotice")}</p>
    </article>
  );
}
