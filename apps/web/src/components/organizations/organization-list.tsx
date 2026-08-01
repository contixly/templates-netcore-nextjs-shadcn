"use client";

import { useTranslations } from "next-intl";
import { useEffect, useReducer, useRef, useState } from "react";

import {
  OrganizationCard,
  type OrganizationCardView,
} from "@/src/components/organizations/organization-card";
import { useOrganizationControlInteractionReady } from "@/src/components/organizations/organization-control-readiness";
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
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getOrganizations } from "@/src/lib/api/generated/sdk.gen";
import type { OrganizationPageResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

export type OrganizationListItem = OrganizationCardView;

export type OrganizationListPage = Readonly<{
  items: readonly OrganizationListItem[];
  nextCursor: string | null;
}>;

type OrganizationListState = Readonly<{
  accumulated: readonly OrganizationListItem[];
  continuationIds: ReadonlySet<string>;
  deletedIds: ReadonlySet<string>;
  loadedContinuation: boolean;
  nextCursor: string | null;
  pending: boolean;
  serverPage: OrganizationListPage;
  continuationFailure?: ApiFailure;
}>;

type OrganizationListAction =
  | Readonly<{
      type: "loadSucceeded";
      page: OrganizationListPage;
    }>
  | Readonly<{
      type: "serverReconciled";
      page: OrganizationListPage;
    }>
  | Readonly<{ type: "loadFailed"; failure: ApiFailure }>
  | Readonly<{ type: "loadStarted" }>
  | Readonly<{ type: "delete"; organizationId: string }>;

function latestUniqueOrganizations(
  organizations: readonly OrganizationListItem[],
): OrganizationListItem[] {
  const byId = new Map<string, OrganizationListItem>();
  const order: string[] = [];
  for (const organization of organizations) {
    if (!byId.has(organization.id)) {
      order.push(organization.id);
    }
    byId.set(organization.id, organization);
  }
  return order.map((id) => byId.get(id)!);
}

function appendAuthoritativeOrganizations(
  accumulated: readonly OrganizationListItem[],
  incoming: readonly OrganizationListItem[],
): OrganizationListItem[] {
  const incomingById = new Map(
    latestUniqueOrganizations(incoming).map(
      (organization) => [organization.id, organization] as const,
    ),
  );
  const merged = accumulated.map(
    (organization) => incomingById.get(organization.id) ?? organization,
  );
  const accumulatedIds = new Set(
    accumulated.map((organization) => organization.id),
  );
  merged.push(
    ...incoming.filter((organization) => !accumulatedIds.has(organization.id)),
  );
  return latestUniqueOrganizations(merged);
}

function authoritativeFirstOrganizations(
  accumulated: readonly OrganizationListItem[],
  incoming: readonly OrganizationListItem[],
  continuationIds: ReadonlySet<string>,
): OrganizationListItem[] {
  const authoritative = latestUniqueOrganizations(incoming);
  const authoritativeIds = new Set(
    authoritative.map((organization) => organization.id),
  );
  return [
    ...authoritative,
    ...accumulated.filter(
      (organization) =>
        continuationIds.has(organization.id) &&
        !authoritativeIds.has(organization.id),
    ),
  ];
}

function compactOrganizationPage(
  page: OrganizationPageResponse,
): OrganizationListPage {
  return {
    items: page.items.map((organization) => ({
      id: organization.id,
      name: organization.name,
      slug: organization.slug,
      canonicalKey: organization.canonicalKey,
      currentRole: organization.currentRole,
      capabilities: {
        canDeleteOrganization: organization.capabilities.canDeleteOrganization,
      },
    })),
    nextCursor: page.nextCursor,
  };
}

function organizationListReducer(
  state: OrganizationListState,
  action: OrganizationListAction,
): OrganizationListState {
  if (action.type === "delete") {
    const deletedIds = new Set(state.deletedIds);
    deletedIds.add(action.organizationId);
    const continuationIds = new Set(state.continuationIds);
    continuationIds.delete(action.organizationId);
    return {
      ...state,
      accumulated: state.accumulated.filter(
        (organization) => organization.id !== action.organizationId,
      ),
      continuationIds,
      deletedIds,
    };
  }

  if (action.type === "loadStarted") {
    return {
      ...state,
      continuationFailure: undefined,
      pending: true,
    };
  }

  if (action.type === "loadFailed") {
    return {
      ...state,
      continuationFailure: action.failure,
      pending: false,
    };
  }

  if (action.type === "serverReconciled") {
    const authoritativeIds = new Set(
      action.page.items.map((organization) => organization.id),
    );
    return {
      ...state,
      accumulated: authoritativeFirstOrganizations(
        state.accumulated,
        action.page.items,
        state.continuationIds,
      ).filter((organization) => !state.deletedIds.has(organization.id)),
      continuationIds: new Set(
        [...state.continuationIds].filter(
          (organizationId) =>
            !authoritativeIds.has(organizationId) &&
            !state.deletedIds.has(organizationId),
        ),
      ),
      serverPage: action.page,
      ...(!state.loadedContinuation
        ? { nextCursor: action.page.nextCursor }
        : {}),
    };
  }

  const continuationIds = new Set(state.continuationIds);
  const authoritativeIds = new Set(
    state.serverPage.items.map((organization) => organization.id),
  );
  for (const organization of action.page.items) {
    if (
      !authoritativeIds.has(organization.id) &&
      !state.deletedIds.has(organization.id)
    ) {
      continuationIds.add(organization.id);
    }
  }
  return {
    ...state,
    accumulated: appendAuthoritativeOrganizations(
      state.accumulated,
      action.page.items,
    ).filter((organization) => !state.deletedIds.has(organization.id)),
    continuationFailure: undefined,
    continuationIds,
    loadedContinuation: true,
    nextCursor: action.page.nextCursor,
    pending: false,
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
  initialPage,
}: Readonly<{
  initialPage: OrganizationListPage;
}>) {
  const t = useTranslations("organizations.list");
  const interactionReady = useOrganizationControlInteractionReady();
  const requestInFlight = useRef(false);
  const [apiClient] = useState(() => createBrowserApiClient());
  const [state, dispatch] = useReducer(organizationListReducer, {
    accumulated: initialPage.items,
    continuationIds: new Set<string>(),
    deletedIds: new Set<string>(),
    loadedContinuation: false,
    nextCursor: initialPage.nextCursor,
    pending: false,
    serverPage: initialPage,
  });
  const serverPageChanged = state.serverPage !== initialPage;
  const organizations = (
    serverPageChanged
      ? authoritativeFirstOrganizations(
          state.accumulated,
          initialPage.items,
          state.continuationIds,
        )
      : state.accumulated
  ).filter((organization) => !state.deletedIds.has(organization.id));
  const nextCursor = state.loadedContinuation
    ? state.nextCursor
    : initialPage.nextCursor;

  useEffect(() => {
    if (state.serverPage === initialPage) {
      return;
    }
    dispatch({ type: "serverReconciled", page: initialPage });
  }, [initialPage, state.serverPage]);

  async function loadMore() {
    if (!interactionReady || !nextCursor || requestInFlight.current) {
      return;
    }

    requestInFlight.current = true;
    dispatch({ type: "loadStarted" });
    try {
      const result = await getOrganizations({
        client: apiClient,
        cache: "no-store",
        query: { cursor: nextCursor },
      });
      if (result.data === undefined) {
        dispatch({
          type: "loadFailed",
          failure: normalizeApiFailure(result.error, result.response),
        });
        return;
      }
      dispatch({
        type: "loadSucceeded",
        page: compactOrganizationPage(result.data.data),
      });
    } catch (error) {
      dispatch({
        type: "loadFailed",
        failure: normalizeApiFailure(error),
      });
    } finally {
      requestInFlight.current = false;
    }
  }

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
      {state.continuationFailure ? (
        <Alert>
          <AlertTitle>{t("partialFailureTitle")}</AlertTitle>
          <AlertDescription>
            <p>{t("partialFailureDescription")}</p>
            {state.continuationFailure.kind === "problem" &&
            state.continuationFailure.traceId ? (
              <p className="font-mono text-xs">
                {state.continuationFailure.traceId}
              </p>
            ) : null}
          </AlertDescription>
        </Alert>
      ) : null}
      {nextCursor ? (
        <div className="flex justify-center">
          <Button
            data-organization-control-interaction-ready={
              interactionReady ? "true" : undefined
            }
            disabled={!interactionReady || state.pending}
            onClick={loadMore}
            type="button"
            variant="outline"
          >
            {state.pending ? t("loadingMore") : t("loadMore")}
          </Button>
        </div>
      ) : null}
    </div>
  );
}
