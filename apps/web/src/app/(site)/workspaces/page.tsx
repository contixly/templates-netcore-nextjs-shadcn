import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import {
  OrganizationFailure,
  OrganizationList,
} from "@/src/components/organizations/organization-list";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";

type WorkspacesPageProps = Readonly<{
  searchParams: Promise<{ cursor?: string | string[] }>;
}>;

export default async function WorkspacesPage({
  searchParams,
}: WorkspacesPageProps) {
  await connection();
  const { cursor } = await searchParams;
  const continuationCursor =
    cursor === undefined
      ? undefined
      : [...new Set(Array.isArray(cursor) ? cursor : [cursor])][0];
  const [session, t, firstPage, continuation] = await Promise.all([
    loadProtectedSession(organizationRoutes.workspaces),
    getTranslations("organizations.pages.workspaces"),
    loadOrganizations(),
    continuationCursor
      ? loadOrganizations({ cursor: continuationCursor })
      : Promise.resolve(undefined),
  ]);

  if (!session.ok) {
    return (
      <main className="mx-auto w-full max-w-5xl px-4 py-12">
        <OrganizationFailure failure={session.failure} />
      </main>
    );
  }
  if (
    session.data.authenticated !== true ||
    !session.data.session ||
    !session.data.user
  ) {
    return (
      <main className="mx-auto w-full max-w-5xl px-4 py-12">
        <OrganizationFailure
          failure={{ kind: "network", code: "api_unavailable" }}
        />
      </main>
    );
  }

  return (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-8 px-4 py-12">
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
          continuationFailure={
            continuation && !continuation.ok ? continuation.failure : undefined
          }
          pages={[
            firstPage.data,
            ...(continuation?.ok ? [continuation.data] : []),
          ]}
        />
      )}
    </main>
  );
}
