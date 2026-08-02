"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { IconKey } from "@tabler/icons-react";

import { ApiKeyCreateDialog } from "@/src/components/api-keys/api-key-create-dialog";
import { ApiKeyEducation } from "@/src/components/api-keys/api-key-education";
import {
  ApiKeySecretView,
  type ApiKeySecretViewHandle,
} from "@/src/components/api-keys/api-key-secret-view";
import { ApiKeyTable } from "@/src/components/api-keys/api-key-table";
import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/components/application/interaction-readiness";
import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import { Button } from "@/src/components/ui/button";
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/src/components/ui/empty";
import {
  apiKeyFailureMessage,
  apiKeyIdentityMismatchFailure,
} from "@/src/features/api-keys/api-key-failures";
import type { ApiKeyOwner } from "@/src/features/api-keys/api-key-routes";
import {
  listBrowserApiKeys,
  updateBrowserApiKey,
} from "@/src/lib/api/api-keys/browser/api-key-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type {
  ApiKeyPageResponse,
  ApiKeyResponse,
} from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

type ConfirmedAction =
  "created" | "updated" | "enabled" | "disabled" | "rotated" | "revoked";
type ConfirmedOverlay = Readonly<{
  apiKey: ApiKeyResponse | null;
}>;

function dedupe(
  current: readonly ApiKeyResponse[],
  incoming: readonly ApiKeyResponse[],
): ApiKeyResponse[] {
  const byId = new Map(current.map((apiKey) => [apiKey.id, apiKey]));
  for (const apiKey of incoming) {
    if (!byId.has(apiKey.id)) byId.set(apiKey.id, apiKey);
  }
  return [...byId.values()];
}

function sameStrings(left: readonly string[], right: readonly string[]) {
  return (
    left.length === right.length &&
    left.every((value, index) => value === right[index])
  );
}

function semanticallyAcknowledges(left: ApiKeyResponse, right: ApiKeyResponse) {
  return (
    left.id === right.id &&
    left.ownerKind === right.ownerKind &&
    left.ownerId === right.ownerId &&
    left.name === right.name &&
    left.start === right.start &&
    left.enabled === right.enabled &&
    sameStrings(left.scopes, right.scopes) &&
    left.rateLimitEnabled === right.rateLimitEnabled &&
    left.rateLimitMax === right.rateLimitMax &&
    left.rateLimitWindow === right.rateLimitWindow &&
    left.expiresAt === right.expiresAt &&
    left.rotatedAt === right.rotatedAt &&
    left.createdAt === right.createdAt &&
    left.updatedAt === right.updatedAt
  );
}

function acknowledgedOverlays(
  current: ReadonlyMap<string, ConfirmedOverlay>,
  firstPage: readonly ApiKeyResponse[],
) {
  const authoritativeById = new Map(
    firstPage.map((apiKey) => [apiKey.id, apiKey]),
  );
  return new Map(
    [...current].filter(([apiKeyId, overlay]) => {
      const authoritative = authoritativeById.get(apiKeyId);
      if (overlay.apiKey === null) return authoritative !== undefined;
      return (
        !authoritative ||
        !semanticallyAcknowledges(authoritative, overlay.apiKey)
      );
    }),
  );
}

function traceId(failure: ApiFailure) {
  return failure.kind === "problem" ? failure.traceId : undefined;
}

export function ApiKeyManagement({
  initialPage,
  owner,
}: Readonly<{
  initialPage: ApiKeyPageResponse;
  owner: ApiKeyOwner;
}>) {
  const t = useTranslations("apiKeys");
  const interactionReady = useInteractionReady();
  const mounted = useRef(true);
  const refreshGeneration = useRef(0);
  const continuationGeneration = useRef(0);
  const toggleGeneration = useRef(0);
  const refreshInFlight = useRef(false);
  const continuationInFlight = useRef(false);
  const toggleInFlight = useRef(false);
  const secretView = useRef<ApiKeySecretViewHandle>(null);
  const overlaysRef = useRef(new Map<string, ConfirmedOverlay>());
  const [authoritative, setAuthoritative] = useState(initialPage.items);
  const [nextCursor, setNextCursor] = useState(initialPage.nextCursor);
  const [overlays, setOverlays] = useState(
    () => new Map<string, ConfirmedOverlay>(),
  );
  const [loadingMore, setLoadingMore] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [pendingKeyId, setPendingKeyId] = useState<string | null>(null);
  const [partialFailure, setPartialFailure] = useState<ApiFailure | null>(null);
  const [mutationFailure, setMutationFailure] = useState<ApiFailure | null>(
    null,
  );
  const [refreshFailure, setRefreshFailure] = useState<{
    action: ConfirmedAction;
    failure: ApiFailure;
  } | null>(null);
  const [feedback, setFeedback] = useState<ConfirmedAction | null>(null);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      refreshGeneration.current += 1;
      continuationGeneration.current += 1;
      toggleGeneration.current += 1;
      refreshInFlight.current = false;
      continuationInFlight.current = false;
      toggleInFlight.current = false;
    };
  }, []);

  const apiKeys = useMemo(() => {
    const byId = new Map(authoritative.map((apiKey) => [apiKey.id, apiKey]));
    for (const [apiKeyId, overlay] of overlays) {
      if (overlay.apiKey === null) byId.delete(apiKeyId);
      else byId.set(apiKeyId, overlay.apiKey);
    }
    return [...byId.values()];
  }, [authoritative, overlays]);

  function setConfirmed(apiKeyId: string, apiKey: ApiKeyResponse | null) {
    const next = new Map(overlaysRef.current);
    next.set(apiKeyId, { apiKey });
    overlaysRef.current = next;
    setOverlays(next);
  }

  async function read(
    cursor: string | undefined,
    kind: "loadMore" | "refresh",
    action?: ConfirmedAction,
    replaceRefresh = false,
  ) {
    let generation: number;
    if (kind === "loadMore") {
      if (refreshInFlight.current || continuationInFlight.current) return;
      continuationInFlight.current = true;
      generation = ++continuationGeneration.current;
      setLoadingMore(true);
      setPartialFailure(null);
    } else {
      if (refreshInFlight.current && !replaceRefresh) return;
      refreshInFlight.current = true;
      generation = ++refreshGeneration.current;
      continuationGeneration.current += 1;
      continuationInFlight.current = false;
      setLoadingMore(false);
      setPartialFailure(null);
      setRefreshing(true);
      setRefreshFailure(null);
    }

    const result = await listBrowserApiKeys(
      createBrowserApiClient(),
      owner,
      cursor ? { cursor } : {},
    );
    const current =
      mounted.current &&
      (kind === "loadMore"
        ? generation === continuationGeneration.current &&
          !refreshInFlight.current
        : generation === refreshGeneration.current);
    if (!current) return;

    if (kind === "loadMore") {
      continuationInFlight.current = false;
      setLoadingMore(false);
    } else {
      refreshInFlight.current = false;
      setRefreshing(false);
    }

    if (!result.ok) {
      if (kind === "loadMore") setPartialFailure(result.failure);
      else if (action) setRefreshFailure({ action, failure: result.failure });
      return;
    }

    if (kind === "loadMore") {
      setAuthoritative((currentItems) =>
        dedupe(currentItems, result.data.items),
      );
    } else {
      setAuthoritative(result.data.items);
      const retained = acknowledgedOverlays(
        overlaysRef.current,
        result.data.items,
      );
      overlaysRef.current = retained;
      setOverlays(retained);
    }
    setNextCursor(result.data.nextCursor);
    setPartialFailure(null);
    setRefreshFailure(null);
    if (action) setFeedback(action);
  }

  function confirmed(apiKey: ApiKeyResponse, action: ConfirmedAction) {
    setMutationFailure(null);
    setConfirmed(apiKey.id, apiKey);
    setFeedback(action);
    void read(undefined, "refresh", action, true);
  }

  function revoked(apiKeyId: string) {
    setMutationFailure(null);
    setConfirmed(apiKeyId, null);
    setFeedback("revoked");
    void read(undefined, "refresh", "revoked", true);
  }

  async function toggle(apiKey: ApiKeyResponse) {
    if (toggleInFlight.current) return;
    toggleInFlight.current = true;
    const generation = ++toggleGeneration.current;
    setPendingKeyId(apiKey.id);
    setFeedback(null);
    setMutationFailure(null);
    const result = await updateBrowserApiKey(
      createBrowserApiClient(),
      owner,
      apiKey.id,
      { enabled: !apiKey.enabled },
    );
    if (!mounted.current || generation !== toggleGeneration.current) return;
    toggleInFlight.current = false;
    setPendingKeyId(null);
    if (!result.ok) {
      setMutationFailure(result.failure);
      return;
    }
    if (result.data.id !== apiKey.id) {
      setMutationFailure(apiKeyIdentityMismatchFailure());
      return;
    }
    confirmed(result.data, result.data.enabled ? "enabled" : "disabled");
  }

  return (
    <div className="flex flex-col gap-8">
      <ApiKeyEducation />
      <section
        aria-labelledby="api-key-list-title"
        className="flex flex-col gap-4"
      >
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-lg font-medium" id="api-key-list-title">
            {t("list.label")}
          </h2>
          <ApiKeyCreateDialog
            onConfirmed={(apiKey) => confirmed(apiKey, "created")}
            owner={owner}
            secretViewRef={secretView}
          />
        </div>

        {feedback && !refreshFailure ? (
          <Alert>
            <AlertTitle>{t(`feedback.${feedback}`)}</AlertTitle>
          </Alert>
        ) : null}
        {mutationFailure ? (
          <Alert variant="destructive">
            <AlertTitle>{t("failures.update")}</AlertTitle>
            <AlertDescription>
              <p>
                {t(`failures.codes.${apiKeyFailureMessage(mutationFailure)}`)}
              </p>
              {traceId(mutationFailure) ? (
                <p className="font-mono">{traceId(mutationFailure)}</p>
              ) : null}
            </AlertDescription>
          </Alert>
        ) : null}
        {refreshFailure ? (
          <Alert variant="destructive">
            <AlertTitle>{t("list.refreshFailure")}</AlertTitle>
            <AlertDescription className="flex flex-col items-start gap-2">
              {traceId(refreshFailure.failure) ? (
                <p className="font-mono">{traceId(refreshFailure.failure)}</p>
              ) : null}
              <Button
                {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
                disabled={!interactionReady || refreshing}
                onClick={() =>
                  void read(undefined, "refresh", refreshFailure.action)
                }
                size="sm"
                type="button"
                variant="outline"
              >
                {refreshing ? t("list.retrying") : t("list.retry")}
              </Button>
            </AlertDescription>
          </Alert>
        ) : null}

        {apiKeys.length === 0 ? (
          <Empty>
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <IconKey />
              </EmptyMedia>
              <EmptyTitle>{t("list.emptyTitle")}</EmptyTitle>
              <EmptyDescription>{t("list.emptyDescription")}</EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : (
          <ApiKeyTable
            apiKeys={apiKeys}
            onConfirmed={(apiKey, action) => confirmed(apiKey, action)}
            onRevoked={revoked}
            onToggle={(apiKey) => void toggle(apiKey)}
            owner={owner}
            pendingKeyId={pendingKeyId}
            secretViewRef={secretView}
          />
        )}

        {partialFailure ? (
          <Alert variant="destructive">
            <AlertTitle>{t("list.partialFailure")}</AlertTitle>
            <AlertDescription>
              {traceId(partialFailure) ? (
                <p className="font-mono">{traceId(partialFailure)}</p>
              ) : null}
              {nextCursor ? (
                <Button
                  {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
                  disabled={!interactionReady || loadingMore || refreshing}
                  onClick={() => void read(nextCursor, "loadMore")}
                  size="sm"
                  type="button"
                  variant="outline"
                >
                  {loadingMore ? t("list.retrying") : t("list.retry")}
                </Button>
              ) : null}
            </AlertDescription>
          </Alert>
        ) : null}
        {nextCursor && !partialFailure ? (
          <Button
            {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
            disabled={!interactionReady || loadingMore || refreshing}
            onClick={() => void read(nextCursor, "loadMore")}
            type="button"
            variant="outline"
          >
            {loadingMore ? t("list.loadingMore") : t("list.loadMore")}
          </Button>
        ) : null}
      </section>
      <ApiKeySecretView ref={secretView} />
    </div>
  );
}
