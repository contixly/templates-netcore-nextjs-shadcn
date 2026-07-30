import { useTranslations } from "next-intl";

import { OrganizationCard } from "@/src/components/organizations/organization-card";
import { OrganizationCreateDialog } from "@/src/components/organizations/organization-create-dialog";
import { Button } from "@/src/components/ui/button";
import type { OrganizationPageResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

export function OrganizationFailure({
  failure,
}: Readonly<{ failure: ApiFailure }>) {
  const t = useTranslations("organizations.failure");

  return (
    <section className="flex flex-col gap-2" role="alert">
      <h2 className="text-lg font-semibold">{t("title")}</h2>
      <p className="text-muted-foreground">{t("description")}</p>
      {failure.kind === "problem" && failure.traceId ? (
        <p className="font-mono text-xs text-muted-foreground">
          {failure.traceId}
        </p>
      ) : null}
    </section>
  );
}

export function OrganizationList({
  loadedCursors = [],
  pages,
}: Readonly<{
  loadedCursors?: readonly string[];
  pages: readonly OrganizationPageResponse[];
}>) {
  const t = useTranslations("organizations.list");
  const seen = new Set<string>();
  const organizations = pages.flatMap((page) =>
    page.items.filter((organization) => {
      if (seen.has(organization.id)) {
        return false;
      }
      seen.add(organization.id);
      return true;
    }),
  );
  const nextCursor = pages.at(-1)?.nextCursor ?? null;

  if (organizations.length === 0) {
    return (
      <section className="flex min-h-72 flex-col items-center justify-center gap-4 border border-dashed p-6 text-center">
        <div className="flex flex-col gap-1">
          <h2 className="text-lg font-semibold">{t("emptyTitle")}</h2>
          <p className="text-muted-foreground">{t("emptyDescription")}</p>
        </div>
        <OrganizationCreateDialog />
      </section>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="grid gap-4 md:grid-cols-2">
        {organizations.map((organization) => (
          <OrganizationCard key={organization.id} organization={organization} />
        ))}
      </div>
      {nextCursor ? (
        <form className="flex justify-center" method="get">
          {loadedCursors.map((cursor, index) => (
            <input
              key={`${index}:${cursor}`}
              name="cursor"
              type="hidden"
              value={cursor}
            />
          ))}
          <input name="cursor" type="hidden" value={nextCursor} />
          <Button type="submit" variant="outline">
            {t("loadMore")}
          </Button>
        </form>
      ) : null}
    </div>
  );
}
