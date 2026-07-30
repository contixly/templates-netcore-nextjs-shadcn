"use client";

import Link from "next/link";
import type { Route } from "next";
import { useTranslations } from "next-intl";
import { useMemo, useReducer } from "react";

import {
  OrganizationCard,
  type OrganizationCardView,
} from "@/src/components/organizations/organization-card";
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
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import type { ApiFailure } from "@/src/lib/api/result";

export type OrganizationListItem = OrganizationCardView;

export type OrganizationListPage = Readonly<{
  items: readonly OrganizationListItem[];
  nextCursor: string | null;
}>;

type OrganizationListState = Readonly<{
  accumulated: readonly OrganizationListItem[];
  deletedIds: ReadonlySet<string>;
}>;

type OrganizationListAction =
  | Readonly<{
      type: "append";
      organizations: readonly OrganizationListItem[];
    }>
  | Readonly<{ type: "delete"; organizationId: string }>;

function uniqueOrganizations(
  organizations: readonly OrganizationListItem[],
): OrganizationListItem[] {
  const seen = new Set<string>();
  return organizations.filter((organization) => {
    if (seen.has(organization.id)) {
      return false;
    }
    seen.add(organization.id);
    return true;
  });
}

function organizationListReducer(
  state: OrganizationListState,
  action: OrganizationListAction,
): OrganizationListState {
  if (action.type === "delete") {
    const deletedIds = new Set(state.deletedIds);
    deletedIds.add(action.organizationId);
    return {
      accumulated: state.accumulated.filter(
        (organization) => organization.id !== action.organizationId,
      ),
      deletedIds,
    };
  }

  return {
    ...state,
    accumulated: uniqueOrganizations([
      ...state.accumulated,
      ...action.organizations,
    ]).filter((organization) => !state.deletedIds.has(organization.id)),
  };
}

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
  pages,
}: Readonly<{
  continuationFailure?: ApiFailure;
  pages: readonly OrganizationListPage[];
}>) {
  const t = useTranslations("organizations.list");
  const incomingOrganizations = useMemo(
    () => uniqueOrganizations(pages.flatMap((page) => page.items)),
    [pages],
  );
  const [state, dispatch] = useReducer(organizationListReducer, {
    accumulated: incomingOrganizations,
    deletedIds: new Set<string>(),
  });
  const organizations = uniqueOrganizations([
    ...state.accumulated,
    ...incomingOrganizations,
  ]).filter((organization) => !state.deletedIds.has(organization.id));
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
      <div className="flex justify-end">
        <OrganizationCreateDialog />
      </div>
      <div className="grid gap-4 md:grid-cols-2">
        {organizations.map((organization) => (
          <OrganizationCard
            canDelete={
              organization.capabilities.canDeleteOrganization &&
              (organizations.some(
                (candidate) => candidate.id !== organization.id,
              ) ||
                nextCursor !== null)
            }
            key={organization.id}
            onDeleted={(organizationId) =>
              dispatch({ type: "delete", organizationId })
            }
            organization={organization}
          />
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
        <div className="flex justify-center">
          <Button asChild variant="outline">
            <Link
              href={
                `${organizationRoutes.workspaces}?cursor=${encodeURIComponent(nextCursor)}` as Route
              }
              onClick={() =>
                dispatch({
                  type: "append",
                  organizations: incomingOrganizations,
                })
              }
              prefetch={false}
            >
              {t("loadMore")}
            </Link>
          </Button>
        </div>
      ) : null}
    </div>
  );
}
