import { getTranslations } from "next-intl/server";

import { AccountInvitationList } from "@/src/components/collaboration/account-invitation-list";
import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { loadAccountInvitations } from "@/src/lib/api/collaboration/server/load-account-invitations";

export default async function AccountInvitationsPage() {
  const [result, t] = await Promise.all([
    loadAccountInvitations({ limit: 20 }),
    getTranslations("collaboration.invitations.account"),
  ]);
  if (!result.ok) return <OrganizationFailure failure={result.failure} />;

  return (
    <article className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold">{t("title")}</h1>
        <p className="text-sm text-muted-foreground">{t("description")}</p>
      </header>
      <AccountInvitationList initialPage={result.data} />
    </article>
  );
}
