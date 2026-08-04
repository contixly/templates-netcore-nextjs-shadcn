import { getTranslations } from "next-intl/server";

import {
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/components/application/settings/settings-shell";

import { AccountInvitationList } from "@/src/components/collaboration/account-invitation-list";
import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { loadAccountInvitations } from "@/src/lib/api/collaboration/server/load-account-invitations";
import { buildApplicationPageMetadata } from "@/src/lib/metadata";

export function generateMetadata() {
  return buildApplicationPageMetadata("accountInvitations");
}

export default async function AccountInvitationsPage() {
  const [result, t] = await Promise.all([
    loadAccountInvitations({ limit: 20 }),
    getTranslations("collaboration.invitations.account"),
  ]);
  if (!result.ok) return <OrganizationFailure failure={result.failure} />;

  return (
    <SettingsPageSection mode="wide">
      <SettingsPageIntro description={t("description")} title={t("title")} />
      <SettingsSection title={t("sectionTitle")}>
        <AccountInvitationList initialPage={result.data} />
      </SettingsSection>
    </SettingsPageSection>
  );
}
