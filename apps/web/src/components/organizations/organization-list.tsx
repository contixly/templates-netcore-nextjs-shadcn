import { useTranslations } from "next-intl";

import { OrganizationCard } from "@/src/components/organizations/organization-card";
import { OrganizationCreateDialog } from "@/src/components/organizations/organization-create-dialog";
import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import { Button } from "@/src/components/ui/button";
import {
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/src/components/ui/empty";
import type { OrganizationPageResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

export function OrganizationFailure({
  failure,
}: Readonly<{ failure: ApiFailure }>) {
  const t = useTranslations("organizations.failure");

  return (
    <Alert>
      <AlertTitle>
        <h2>{t("title")}</h2>
      </AlertTitle>
      <AlertDescription>
        <p>{t("description")}</p>
        {failure.kind === "problem" && failure.traceId ? (
          <p className="font-mono text-xs">{failure.traceId}</p>
        ) : null}
      </AlertDescription>
    </Alert>
  );
}

export function OrganizationList({
  continuationFailure,
  loadedCursors = [],
  pages,
}: Readonly<{
  continuationFailure?: ApiFailure;
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
      <Empty className="min-h-72 border">
        <EmptyHeader>
          <EmptyTitle>
            <h2>{t("emptyTitle")}</h2>
          </EmptyTitle>
          <EmptyDescription>{t("emptyDescription")}</EmptyDescription>
        </EmptyHeader>
        <EmptyContent>
          <OrganizationCreateDialog />
        </EmptyContent>
      </Empty>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="grid gap-4 md:grid-cols-2">
        {organizations.map((organization) => (
          <OrganizationCard key={organization.id} organization={organization} />
        ))}
      </div>
      {continuationFailure ? (
        <Alert>
          <AlertTitle>{t("partialFailureTitle")}</AlertTitle>
          <AlertDescription>
            <p>{t("partialFailureDescription")}</p>
            {continuationFailure.kind === "problem" &&
            continuationFailure.traceId ? (
              <p className="font-mono text-xs">{continuationFailure.traceId}</p>
            ) : null}
          </AlertDescription>
        </Alert>
      ) : null}
      {nextCursor && !continuationFailure ? (
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
