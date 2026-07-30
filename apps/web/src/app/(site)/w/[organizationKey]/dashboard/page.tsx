import { forbidden } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import { BrowserSessionRefresh } from "@/src/components/authentication/browser-session-refresh";
import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { OrganizationOnboarding } from "@/src/components/organizations/organization-onboarding";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
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
  const [organization, organizations, t] = await Promise.all([
    loadOrganization(organizationKey),
    loadOrganizations(),
    getTranslations("organizations.pages.dashboard"),
  ]);

  if (!organizations.ok) {
    return <OrganizationFailure failure={organizations.failure} />;
  }
  if (organizations.data.items.length === 0) {
    return <OrganizationOnboarding />;
  }
  if (!organization.ok) {
    if (
      organization.failure.kind === "problem" &&
      organization.failure.status === 404
    ) {
      forbidden();
    }
    return <OrganizationFailure failure={organization.failure} />;
  }

  return (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-8 px-4 py-12">
      <BrowserSessionRefresh />
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
