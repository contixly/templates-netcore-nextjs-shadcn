import { forbidden, redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { OrganizationOnboarding } from "@/src/components/organizations/organization-onboarding";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";

type OrganizationDashboardPageProps = Readonly<{
  params: Promise<{ organizationKey: string }>;
}>;

export default async function OrganizationDashboardPage({
  params,
}: OrganizationDashboardPageProps) {
  await connection();
  const { organizationKey } = await params;
  const route = organizationRoutes.dashboard(organizationKey);
  const sessionPromise = loadProtectedSession(route);
  const organizationPromise = loadOrganization(organizationKey);
  const organizationsPromise = loadOrganizations();
  const translationsPromise = getTranslations("organizations.pages.dashboard");
  const session = await sessionPromise;

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
  const organization = await organizationPromise;
  if (!organization.ok) {
    if (
      organization.failure.kind === "problem" &&
      organization.failure.status === 404
    ) {
      const organizations = await organizationsPromise;
      if (!organizations.ok) {
        return <OrganizationFailure failure={organizations.failure} />;
      }
      if (organizations.data.items.length === 0) {
        return <OrganizationOnboarding />;
      }
      forbidden();
    }
    return <OrganizationFailure failure={organization.failure} />;
  }
  if (organizationKey !== organization.data.canonicalKey) {
    redirect(organizationRoutes.dashboard(organization.data.canonicalKey));
  }
  const t = await translationsPromise;

  return (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-8 px-4 py-12">
      <div className="flex flex-col gap-2">
        <h1 className="text-3xl font-semibold tracking-tight">{t("title")}</h1>
        <p className="text-muted-foreground">{t("description")}</p>
      </div>
      <Card className="max-w-2xl">
        <CardHeader>
          <CardTitle>{organization.data.name}</CardTitle>
        </CardHeader>
        <CardContent>
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2">
            <dt className="text-muted-foreground">{t("name")}</dt>
            <dd>{organization.data.name}</dd>
            <dt className="text-muted-foreground">{t("slug")}</dt>
            <dd>
              <code>{organization.data.slug}</code>
            </dd>
          </dl>
        </CardContent>
      </Card>
    </main>
  );
}
