import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import {
  OrganizationFailure,
  OrganizationList,
} from "@/src/components/organizations/organization-list";
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
  const [session, t, firstPage] = await Promise.all([
    loadProtectedSession(organizationRoutes.workspaces),
    getTranslations("organizations.pages.workspaces"),
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
    <section className="mx-auto flex w-full max-w-5xl flex-col gap-8 px-4 py-12">
      <div className="flex flex-col gap-2">
        <h1 className="text-3xl font-semibold tracking-tight">{t("title")}</h1>
        <p className="text-muted-foreground">{t("description")}</p>
      </div>
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
