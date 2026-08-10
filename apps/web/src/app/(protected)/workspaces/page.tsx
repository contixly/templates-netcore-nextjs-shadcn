import { redirect } from "next/navigation";
import { connection } from "next/server";

import {
  OrganizationFailure,
  OrganizationList,
} from "@/src/features/organizations/ui/organization-list";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";
import type { OrganizationPageResponse } from "@/src/lib/api/generated/types.gen";
import { buildApplicationPageMetadata } from "@/src/lib/metadata";

function compactOrganizationPage(page: OrganizationPageResponse) {
  return {
    items: page.items.flatMap((organization) =>
      organization.accessPrincipal === "user"
        ? [
            {
              id: organization.id,
              name: organization.name,
              slug: organization.slug,
              canonicalKey: organization.canonicalKey,
              currentRole: organization.currentRole,
              capabilities: {
                canDeleteOrganization:
                  organization.capabilities.canDeleteOrganization,
              },
            },
          ]
        : [],
    ),
    nextCursor: page.nextCursor,
  };
}

type WorkspacesPageProps = Readonly<{
  searchParams: Promise<{ cursor?: string | string[] }>;
}>;

export function generateMetadata() {
  return buildApplicationPageMetadata("workspaces");
}

export default async function WorkspacesPage({
  searchParams,
}: WorkspacesPageProps) {
  await connection();
  const { cursor } = await searchParams;
  if (cursor !== undefined) {
    redirect(organizationRoutes.workspaces);
  }
  const [session, firstPage] = await Promise.all([
    loadProtectedSession(organizationRoutes.workspaces),
    loadOrganizations(),
  ]);

  if (!session.ok) {
    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-12">
        <OrganizationFailure failure={session.failure} />
      </div>
    );
  }
  if (
    session.data.authenticated !== true ||
    !session.data.session ||
    !session.data.user
  ) {
    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-12">
        <OrganizationFailure
          failure={{ kind: "network", code: "api_unavailable" }}
        />
      </div>
    );
  }

  return (
    <section className="mx-auto flex w-full max-w-[1360px] flex-col px-4 py-8 lg:px-6">
      {!firstPage || !firstPage.ok ? (
        <OrganizationFailure
          failure={
            firstPage?.failure ?? {
              kind: "network",
              code: "api_unavailable",
            }
          }
        />
      ) : (
        <OrganizationList
          initialPage={compactOrganizationPage(firstPage.data)}
        />
      )}
    </section>
  );
}
