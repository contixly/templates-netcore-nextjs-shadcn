import { connection } from "next/server";

import { OrganizationSwitcher } from "@/src/components/organizations/organization-switcher";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";

export type WorkspaceOrganizationSwitcherSlotProps = Readonly<{
  params: Promise<{
    organizationKey: string;
    path?: string[];
  }>;
}>;

export async function WorkspaceOrganizationSwitcherSlot({
  params,
}: WorkspaceOrganizationSwitcherSlotProps) {
  await connection();
  const { organizationKey } = await params;
  const [session, organization, organizations] = await Promise.all([
    loadServerAuthSession(),
    loadOrganization(organizationKey),
    loadOrganizations(),
  ]);

  if (
    !session.ok ||
    session.data.authenticated !== true ||
    !session.data.session ||
    !organization.ok ||
    !organizations.ok
  ) {
    return null;
  }

  return (
    <OrganizationSwitcher
      activeOrganizationId={session.data.session.activeOrganizationId}
      currentOrganization={{
        canManageInvitations:
          organization.data.capabilities.canManageInvitations,
        canonicalKey: organization.data.canonicalKey,
        id: organization.data.id,
        name: organization.data.name,
      }}
      nextCursor={organizations.data.nextCursor}
      organizations={organizations.data.items.map(
        ({ capabilities, canonicalKey, id, name }) => ({
          canManageInvitations: capabilities.canManageInvitations,
          canonicalKey,
          id,
          name,
        }),
      )}
    />
  );
}
