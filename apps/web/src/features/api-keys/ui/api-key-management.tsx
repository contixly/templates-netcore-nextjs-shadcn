"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { IconKey } from "@tabler/icons-react";

import { ApiKeyCreateDialog } from "@/src/features/api-keys/ui/api-key-create-dialog";
import { ApiKeyEducation } from "@/src/features/api-keys/ui/api-key-education";
import {
  ApiKeySecretView,
  type ApiKeySecretViewHandle,
} from "@/src/features/api-keys/ui/api-key-secret-view";
import { ApiKeyTable } from "@/src/features/api-keys/ui/api-key-table";
import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/features/application/ui/interaction-readiness";
import { SettingsSection } from "@/src/features/application/ui/settings/settings-shell";
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
  apiKeyMutationBusyFailure,
} from "@/src/features/api-keys/api-key-failures";
import type {
  ApiKeyMutationArbiter,
  ApiKeyMutationLease,
} from "@/src/features/api-keys/api-key-mutation-arbiter";
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
type AuthoritativeTraversal = Readonly<{
  byId: ReadonlyMap<string, ApiKeyResponse>;
  terminal: boolean;
}>;

function authoritativeTraversal(
  current: AuthoritativeTraversal | null,
  incoming: readonly ApiKeyResponse[],
  nextCursor: string | null,
): AuthoritativeTraversal {
  const byId = new Map(current?.byId);
  for (const apiKey of incoming) {
    if (!byId.has(apiKey.id)) byId.set(apiKey.id, apiKey);
  }
  return { byId, terminal: nextCursor === null };
}

function rfc3339Instant(value: string): readonly [number, number] | null {
  const match =
    /^(\d{4})-(\d{2})-(\d{2})[Tt](\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,9}))?([Zz]|([+-])(\d{2}):(\d{2}))$/.exec(
      value,
    );
  if (!match) return null;

  const [year, month, day, hour, minute, second, offsetHour, offsetMinute] = [
    match[1],
    match[2],
    match[3],
    match[4],
    match[5],
    match[6],
    match[10],
    match[11],
  ].map(Number);
  const leapYear = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const daysInMonth = [
    31,
    leapYear ? 29 : 28,
    31,
    30,
    31,
    30,
    31,
    31,
    30,
    31,
    30,
    31,
  ];
  if (
    year < 1 ||
    month < 1 ||
    month > 12 ||
    day < 1 ||
    day > daysInMonth[month - 1]! ||
    hour > 23 ||
    minute > 59 ||
    second > 59 ||
    (match[9] !== undefined && (offsetHour > 23 || offsetMinute > 59))
  ) {
    return null;
  }

  const zone = match[8]!.toUpperCase() === "Z" ? "Z" : match[8]!;
  const milliseconds = Date.parse(
    `${match[1]}-${match[2]}-${match[3]}T${match[4]}:${match[5]}:${match[6]}${zone}`,
  );
  if (!Number.isFinite(milliseconds)) return null;

  const nanoseconds = Number((match[7] ?? "").padEnd(9, "0"));
  return [milliseconds, nanoseconds];
}

function sameOrNewer(
  authoritative: readonly [number, number],
  confirmed: readonly [number, number],
) {
  return (
    authoritative[0] > confirmed[0] ||
    (authoritative[0] === confirmed[0] && authoritative[1] >= confirmed[1])
  );
}

function isSameOwner(left: ApiKeyResponse, right: ApiKeyResponse) {
  return left.ownerKind === right.ownerKind && left.ownerId === right.ownerId;
}

function reconciledOverlays(
  current: ReadonlyMap<string, ConfirmedOverlay>,
  traversal: AuthoritativeTraversal,
) {
  const retained = new Map(current);
  for (const [apiKeyId, overlay] of current) {
    const authoritative = traversal.byId.get(apiKeyId);
    if (overlay.apiKey === null) {
      if (traversal.terminal && authoritative === undefined) {
        retained.delete(apiKeyId);
      }
      continue;
    }

    if (!authoritative || !isSameOwner(authoritative, overlay.apiKey)) {
      continue;
    }
    const authoritativeTime = rfc3339Instant(authoritative.updatedAt);
    const confirmedTime = rfc3339Instant(overlay.apiKey.updatedAt);
    if (
      authoritativeTime !== null &&
      confirmedTime !== null &&
      sameOrNewer(authoritativeTime, confirmedTime)
    ) {
      retained.delete(apiKeyId);
    }
  }
  return retained;
}

function traceId(failure: ApiFailure) {
  return failure.kind === "problem" ? failure.traceId : undefined;
}

export function ApiKeyManagement({
  headingLevel = 2,
  initialPage,
  owner,
  showListHeading = true,
}: Readonly<{
  headingLevel?: 2 | 3;
  initialPage: ApiKeyPageResponse;
  owner: ApiKeyOwner;
  showListHeading?: boolean;
}>) {
  const t = useTranslations("apiKeys");
  const interactionReady = useInteractionReady();
  const mounted = useRef(true);
  const refreshGeneration = useRef(0);
  const continuationGeneration = useRef(0);
  const refreshInFlight = useRef(false);
  const continuationInFlight = useRef(false);
  const nextMutationLease = useRef(0);
  const activeMutationLeases = useRef(new Map<string, number>());
  const secretView = useRef<ApiKeySecretViewHandle>(null);
  const overlaysRef = useRef(new Map<string, ConfirmedOverlay>());
  const traversalRef = useRef<AuthoritativeTraversal>(
    authoritativeTraversal(null, initialPage.items, initialPage.nextCursor),
  );
  const [authoritative, setAuthoritative] = useState(initialPage.items);
  const [nextCursor, setNextCursor] = useState(initialPage.nextCursor);
  const [overlays, setOverlays] = useState(
    () => new Map<string, ConfirmedOverlay>(),
  );
  const [loadingMore, setLoadingMore] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [busyKeyIds, setBusyKeyIds] = useState<ReadonlySet<string>>(
    () => new Set(),
  );
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
    const mutationLeases = activeMutationLeases.current;
    mounted.current = true;
    return () => {
      mounted.current = false;
      refreshGeneration.current += 1;
      continuationGeneration.current += 1;
      refreshInFlight.current = false;
      continuationInFlight.current = false;
      mutationLeases.clear();
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

  const mutationArbiter: ApiKeyMutationArbiter = {
    acquire(apiKeyId) {
      if (!mounted.current || activeMutationLeases.current.has(apiKeyId)) {
        return null;
      }
      const generation = ++nextMutationLease.current;
      activeMutationLeases.current.set(apiKeyId, generation);
      setBusyKeyIds(new Set(activeMutationLeases.current.keys()));
      return { apiKeyId, generation };
    },
    isCurrent(lease) {
      return (
        mounted.current &&
        activeMutationLeases.current.get(lease.apiKeyId) === lease.generation
      );
    },
    release(lease) {
      if (
        activeMutationLeases.current.get(lease.apiKeyId) !== lease.generation
      ) {
        return;
      }
      activeMutationLeases.current.delete(lease.apiKeyId);
      if (mounted.current) {
        setBusyKeyIds(new Set(activeMutationLeases.current.keys()));
      }
    },
  };

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

    const traversal = authoritativeTraversal(
      kind === "loadMore" ? traversalRef.current : null,
      result.data.items,
      result.data.nextCursor,
    );
    traversalRef.current = traversal;
    setAuthoritative([...traversal.byId.values()]);
    const retained = reconciledOverlays(overlaysRef.current, traversal);
    overlaysRef.current = retained;
    setOverlays(retained);
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
    const lease: ApiKeyMutationLease | null = mutationArbiter.acquire(
      apiKey.id,
    );
    if (!lease) {
      setMutationFailure(apiKeyMutationBusyFailure());
      return;
    }
    setFeedback(null);
    setMutationFailure(null);
    try {
      const result = await updateBrowserApiKey(
        createBrowserApiClient(),
        owner,
        apiKey.id,
        { enabled: !apiKey.enabled },
      );
      if (!mounted.current || !mutationArbiter.isCurrent(lease)) return;
      if (!result.ok) {
        setMutationFailure(result.failure);
        return;
      }
      if (result.data.id !== apiKey.id) {
        setMutationFailure(apiKeyIdentityMismatchFailure());
        return;
      }
      confirmed(result.data, result.data.enabled ? "enabled" : "disabled");
    } finally {
      mutationArbiter.release(lease);
    }
  }

  const createDialog = (
    <ApiKeyCreateDialog
      onConfirmed={(apiKey) => confirmed(apiKey, "created")}
      owner={owner}
      secretViewRef={secretView}
    />
  );
  const listContent = (
    <div className="flex min-w-0 flex-col gap-4">
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
        <Empty className="border">
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
          busyKeyIds={busyKeyIds}
          mutationArbiter={mutationArbiter}
          onConfirmed={(apiKey, action) => confirmed(apiKey, action)}
          onRevoked={revoked}
          onToggle={(apiKey) => void toggle(apiKey)}
          owner={owner}
          secretViewRef={secretView}
        />
      )}

      {partialFailure ? (
        <Alert variant="destructive">
          <AlertTitle>{t("list.partialFailure")}</AlertTitle>
          <AlertDescription className="flex flex-col items-start gap-2">
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
          className="self-start"
          disabled={!interactionReady || loadingMore || refreshing}
          onClick={() => void read(nextCursor, "loadMore")}
          type="button"
          variant="outline"
        >
          {loadingMore ? t("list.loadingMore") : t("list.loadMore")}
        </Button>
      ) : null}
    </div>
  );
  const description =
    owner.kind === "organization"
      ? t("list.organizationDescription")
      : t("list.personalDescription");

  return (
    <div className="flex flex-col gap-4">
      <ApiKeyEducation headingLevel={headingLevel} owner={owner} />
      {showListHeading ? (
        <SettingsSection
          action={createDialog}
          description={description}
          headingLevel={headingLevel}
          title={t("list.label")}
        >
          {listContent}
        </SettingsSection>
      ) : (
        <section aria-label={t("list.label")} className="flex flex-col gap-4">
          <div className="flex justify-end">{createDialog}</div>
          {listContent}
        </section>
      )}
      <ApiKeySecretView ref={secretView} />
    </div>
  );
}
