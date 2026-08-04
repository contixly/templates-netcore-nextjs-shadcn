import { forbidden } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";
import { Suspense, type ReactNode } from "react";

import {
  SettingsContentRail,
  SettingsPageShell,
} from "@/src/components/application/settings/settings-shell";
import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { OrganizationOnboarding } from "@/src/components/organizations/organization-onboarding";
import { OrganizationSettingsNav } from "@/src/components/organizations/organization-settings-nav";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";

type OrganizationSettingsLayoutProps = Readonly<{
  children: ReactNode;
  params: Promise<{ organizationKey: string }>;
}>;

export async function AuthenticatedOrganizationSettingsShell({
  children,
  params,
}: OrganizationSettingsLayoutProps) {
  await connection();
  const { organizationKey } = await params;
  const sessionPromise = loadServerAuthSession();
  const organizationPromise = loadOrganization(organizationKey);
  const organizationsPromise = loadOrganizations();
  const session = await sessionPromise;

  if (!session.ok) {
    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-12">
        <OrganizationFailure failure={session.failure} />
      </div>
    );
  }
  if (session.data.authenticated === false) {
    return children;
  }
  if (!session.data.session || !session.data.user) {
    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-12">
        <OrganizationFailure
          failure={{ kind: "network", code: "api_unavailable" }}
        />
      </div>
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
        return (
          <div className="mx-auto w-full max-w-5xl px-4 py-12">
            <OrganizationFailure failure={organizations.failure} />
          </div>
        );
      }
      if (organizations.data.items.length === 0) {
        return <OrganizationOnboarding />;
      }
      forbidden();
    }

    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-12">
        <OrganizationFailure failure={organization.failure} />
      </div>
    );
  }

  return (
    <SettingsPageShell>
      <OrganizationSettingsNav
        canManageApiKeys={organization.data.capabilities.canManageApiKeys}
        canManageInvitations={
          organization.data.capabilities.canManageInvitations
        }
        organizationKey={organization.data.canonicalKey}
      />
      <SettingsContentRail>{children}</SettingsContentRail>
    </SettingsPageShell>
  );
}

export default async function OrganizationSettingsLayout(
  props: OrganizationSettingsLayoutProps,
) {
  const t = await getTranslations("organizations.settings.navigation");
  return (
    <Suspense
      fallback={
        <p className="mx-auto w-full max-w-5xl px-4 py-12" role="status">
          {t("loading")}
        </p>
      }
    >
      <AuthenticatedOrganizationSettingsShell {...props} />
    </Suspense>
  );
}
