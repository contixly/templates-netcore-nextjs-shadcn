import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import {
  OrganizationFailure,
  OrganizationList,
} from "@/src/components/organizations/organization-list";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";

type WorkspacesPageProps = Readonly<{
  searchParams: Promise<{ cursor?: string | string[] }>;
}>;

export default async function WorkspacesPage({
  searchParams,
}: WorkspacesPageProps) {
  await connection();
  const { cursor } = await searchParams;
  const loadedCursors =
    cursor === undefined ? [] : Array.isArray(cursor) ? cursor : [cursor];
  const t = await getTranslations("organizations.pages.workspaces");
  const results = await Promise.all([
    loadOrganizations(),
    ...loadedCursors.map((value) => loadOrganizations({ cursor: value })),
  ]);
  const failure = results.find((result) => !result.ok);

  return (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-8 px-4 py-12">
      <div className="flex flex-col gap-2">
        <h1 className="text-3xl font-semibold tracking-tight">{t("title")}</h1>
        <p className="text-muted-foreground">{t("description")}</p>
      </div>
      {failure && !failure.ok ? (
        <OrganizationFailure failure={failure.failure} />
      ) : (
        <OrganizationList
          loadedCursors={loadedCursors}
          pages={results.flatMap((result) => (result.ok ? [result.data] : []))}
        />
      )}
    </main>
  );
}
