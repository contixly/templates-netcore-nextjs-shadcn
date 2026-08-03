import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/components/application/settings/settings-shell";

import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { OrganizationMemberDirectory } from "@/src/components/organizations/organization-member-directory";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { evaluateOrganizationEmailDomainEligibility } from "@/src/features/organizations/organization-email-domain-policy";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import type { OrganizationMemberResponse } from "@/src/lib/api/generated/types.gen";
import { loadOrganizationMembers } from "@/src/lib/api/organizations/server/load-organization-members";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";

type OrganizationUsersSettingsPageProps = Readonly<{
  params: Promise<{ organizationKey: string }>;
}>;

function compactMember(member: OrganizationMemberResponse) {
  return {
    id: member.id,
    userId: member.userId,
    name: member.name,
    email: member.email,
    role: member.role,
    joinedAt: member.joinedAt,
    isOutsideAllowedEmailDomains: member.isOutsideAllowedEmailDomains,
  };
}

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
  const actorEligibility = evaluateOrganizationEmailDomainEligibility(
    session.data.user.email,
    organization.data.allowedEmailDomains,
  );

  return (
    <SettingsPageSection mode="wide">
      <SettingsPageIntro description={t("description")} title={t("title")} />
      <SettingsSection title={t("sectionTitle")}>
        <OrganizationMemberDirectory
          key={organization.data.id}
          currentActor={{
            userId: session.data.user.id,
            name: session.data.user.name,
            email: session.data.user.email,
            role: organization.data.currentRole,
            isOutsideAllowedEmailDomains: !actorEligibility.isAllowed,
          }}
          initialPage={{
            items: members.data.items.map(compactMember),
            nextCursor: members.data.nextCursor,
          }}
          headingLevel={3}
          organization={{
            id: organization.data.id,
            currentRole: organization.data.currentRole,
            capabilities: {
              canAddMembers: organization.data.capabilities.canAddMembers,
              canUpdateMemberRoles:
                organization.data.capabilities.canUpdateMemberRoles,
            },
          }}
        />
      </SettingsSection>
    </SettingsPageSection>
  );
}
