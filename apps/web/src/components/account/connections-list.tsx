"use client";

import { useLocale, useTranslations } from "next-intl";
import { useState } from "react";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/components/application/interaction-readiness";
import { Badge } from "@/src/components/ui/badge";
import { Button } from "@/src/components/ui/button";
import { disconnectBrowserAccountProvider } from "@/src/lib/api/account/browser/account-mutations";
import { startExternalAuth } from "@/src/lib/api/auth/browser/start-external-auth";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import {
  getAccountConnections,
  type AccountConnectionResponse,
  type AccountConnectionsResponse,
} from "@/src/lib/api/generated";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import type { ApiFailure } from "@/src/lib/api/result";

type Feedback =
  | { kind: "failure"; message: string; traceId?: string }
  | { kind: "success"; message: string }
  | null;

type RefreshRecovery = Readonly<{
  provider: string;
  traceId?: string;
}>;

function formattedDate(value: string, locale: string): string {
  const date = new Date(value);
  return Number.isNaN(date.valueOf())
    ? value
    : new Intl.DateTimeFormat(locale, {
        dateStyle: "medium",
        timeZone: "UTC",
      }).format(date);
}

function failureTrace(failure: ApiFailure): string | undefined {
  return failure.kind === "problem" ? failure.traceId : undefined;
}

function safeAuthorizationUrl(value: string): string | undefined {
  try {
    const url = new URL(value);
    return url.protocol === "https:" &&
      url.hostname.length > 0 &&
      !url.username &&
      !url.password
      ? url.href
      : undefined;
  } catch {
    return undefined;
  }
}

function conservativeDisconnectProjection(
  current: AccountConnectionResponse[],
  disconnectedProvider: AccountConnectionResponse["provider"],
): AccountConnectionResponse[] {
  const disconnected = current.flatMap((connection) => {
    if (connection.provider !== disconnectedProvider) {
      return [connection];
    }

    return connection.configured
      ? [
          {
            ...connection,
            connected: false,
            email: null,
            connectedAt: null,
            lastUsedAt: null,
            isCurrentAuthenticationMethod: false,
            canConnect: true,
            canDisconnect: false,
            disabledReason: null,
          },
        ]
      : [];
  });

  return disconnected.map((connection) => {
    const configuredSurvivorCount = disconnected.filter(
      (candidate) =>
        candidate.provider !== connection.provider &&
        candidate.configured &&
        candidate.connected,
    ).length;
    const canDisconnect =
      connection.connected &&
      !connection.isCurrentAuthenticationMethod &&
      configuredSurvivorCount > 0;

    return {
      ...connection,
      canConnect: connection.configured && !connection.connected,
      canDisconnect,
      disabledReason:
        connection.connected && !canDisconnect
          ? "external_connection_required"
          : null,
    };
  });
}

export function ConnectionsList({
  initialConnections,
}: Readonly<{ initialConnections: AccountConnectionsResponse }>) {
  const t = useTranslations("account.connections");
  const locale = useLocale();
  const interactionReady = useInteractionReady();
  const [connections, setConnections] = useState(initialConnections.items);
  const [pendingProvider, setPendingProvider] = useState<
    AccountConnectionResponse["provider"] | null
  >(null);
  const [feedback, setFeedback] = useState<Feedback>(null);
  const [refreshRecovery, setRefreshRecovery] =
    useState<RefreshRecovery | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  async function connect(connection: AccountConnectionResponse) {
    if (
      pendingProvider ||
      refreshing ||
      !connection.configured ||
      !connection.canConnect
    ) {
      return;
    }

    setPendingProvider(connection.provider);
    setFeedback(null);
    const result = await startExternalAuth({
      provider: connection.provider,
      intent: "connect",
      returnUrl: "/user/connections",
    });

    if (!result.ok) {
      setFeedback({
        kind: "failure",
        message: t("connectedNavigationFailure"),
        traceId: failureTrace(result.failure),
      });
      setPendingProvider(null);
      return;
    }

    const authorizationUrl = safeAuthorizationUrl(result.data.authorizationUrl);
    if (!authorizationUrl) {
      setFeedback({
        kind: "failure",
        message: t("invalidAuthorizationUrl"),
      });
      setPendingProvider(null);
      return;
    }

    try {
      window.location.assign(authorizationUrl);
    } catch {
      setFeedback({
        kind: "failure",
        message: t("invalidAuthorizationUrl"),
      });
      setPendingProvider(null);
    }
  }

  async function refreshConnections(provider: string) {
    setRefreshing(true);
    try {
      const refreshed = await getAccountConnections({
        client: createBrowserApiClient(),
        cache: "no-store",
      });
      if (refreshed.data === undefined) {
        const failure = normalizeApiFailure(
          refreshed.error,
          refreshed.response,
        );
        setFeedback(null);
        setRefreshRecovery({
          provider,
          traceId: failureTrace(failure),
        });
        return;
      }

      setConnections(refreshed.data.data.items);
      setRefreshRecovery(null);
      setFeedback({
        kind: "success",
        message: t("disconnectSuccess", { provider }),
      });
    } catch (error) {
      const failure = normalizeApiFailure(error);
      setFeedback(null);
      setRefreshRecovery({
        provider,
        traceId: failureTrace(failure),
      });
    } finally {
      setRefreshing(false);
    }
  }

  async function disconnect(connection: AccountConnectionResponse) {
    if (pendingProvider || refreshing || !connection.canDisconnect) {
      return;
    }

    setPendingProvider(connection.provider);
    setFeedback(null);
    setRefreshRecovery(null);
    const result = await disconnectBrowserAccountProvider(
      createBrowserApiClient(),
      connection.provider,
    );

    if (!result.ok) {
      setPendingProvider(null);
      setFeedback({
        kind: "failure",
        message: t("disconnectFailure"),
        traceId: failureTrace(result.failure),
      });
      return;
    }

    setConnections((current) =>
      conservativeDisconnectProjection(current, result.data.provider),
    );
    setPendingProvider(null);
    await refreshConnections(connection.displayName);
  }

  return (
    <div className="flex flex-col gap-4">
      {feedback ? (
        <div
          className={
            feedback.kind === "failure"
              ? "flex flex-col gap-1 text-sm text-destructive"
              : "text-sm"
          }
          role={feedback.kind === "failure" ? "alert" : "status"}
        >
          <p>{feedback.message}</p>
          {feedback.kind === "failure" && feedback.traceId ? (
            <p className="font-mono text-xs">{feedback.traceId}</p>
          ) : null}
        </div>
      ) : null}

      {refreshRecovery ? (
        <div
          className="flex flex-col items-start gap-2 text-sm text-destructive"
          role="alert"
        >
          <p>
            {t("disconnectRefreshFailure", {
              provider: refreshRecovery.provider,
            })}
          </p>
          {refreshRecovery.traceId ? (
            <p className="font-mono text-xs">{refreshRecovery.traceId}</p>
          ) : null}
          <Button
            {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
            disabled={!interactionReady || refreshing}
            onClick={() => void refreshConnections(refreshRecovery.provider)}
            type="button"
            variant="outline"
          >
            {refreshing ? t("refreshing") : t("retryRefresh")}
          </Button>
        </div>
      ) : null}

      <div className="grid gap-3">
        {connections.map((connection) => {
          const pending = pendingProvider === connection.provider;
          const disconnectedReason = connection.disabledReason
            ? t(`disabledReasons.${connection.disabledReason}`)
            : t("disabledReasons.unknown");

          return (
            <article
              aria-label={t("connectionLabel", {
                provider: connection.displayName,
              })}
              className="flex flex-col gap-4 border p-4 sm:flex-row sm:items-start sm:justify-between"
              key={connection.provider}
            >
              <div className="flex min-w-0 flex-col gap-2">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-sm font-semibold">
                    {connection.displayName}
                  </h2>
                  <Badge
                    variant={connection.connected ? "secondary" : "outline"}
                  >
                    {connection.connected ? t("connected") : t("notConnected")}
                  </Badge>
                  {connection.isCurrentAuthenticationMethod ? (
                    <Badge>{t("currentMethod")}</Badge>
                  ) : null}
                </div>

                {!connection.configured ? (
                  <p className="text-xs text-muted-foreground">
                    {t("configurationUnavailable")}
                  </p>
                ) : null}
                {connection.email ? (
                  <p className="text-xs break-all text-muted-foreground">
                    {t("email", { email: connection.email })}
                  </p>
                ) : null}
                {connection.connectedAt ? (
                  <p className="text-xs text-muted-foreground">
                    {t("connectedAt", {
                      date: formattedDate(connection.connectedAt, locale),
                    })}
                  </p>
                ) : null}
                {connection.connected ? (
                  <p className="text-xs text-muted-foreground">
                    {connection.lastUsedAt
                      ? t("lastUsedAt", {
                          date: formattedDate(connection.lastUsedAt, locale),
                        })
                      : t("neverUsed")}
                  </p>
                ) : null}
                {connection.connected && !connection.canDisconnect ? (
                  <p className="text-xs text-muted-foreground">
                    {disconnectedReason}
                  </p>
                ) : null}
              </div>

              <div className="shrink-0">
                {connection.connected ? (
                  <Button
                    {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
                    aria-label={t("disconnect", {
                      provider: connection.displayName,
                    })}
                    disabled={
                      !interactionReady ||
                      pendingProvider !== null ||
                      refreshing ||
                      !connection.canDisconnect
                    }
                    onClick={() => void disconnect(connection)}
                    type="button"
                    variant="outline"
                  >
                    {pending
                      ? t("disconnecting", {
                          provider: connection.displayName,
                        })
                      : t("disconnect", {
                          provider: connection.displayName,
                        })}
                  </Button>
                ) : (
                  <Button
                    {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
                    aria-label={t("connect", {
                      provider: connection.displayName,
                    })}
                    disabled={
                      !interactionReady ||
                      pendingProvider !== null ||
                      refreshing ||
                      !connection.configured ||
                      !connection.canConnect
                    }
                    onClick={() => void connect(connection)}
                    type="button"
                    variant="outline"
                  >
                    {pending
                      ? t("connecting", {
                          provider: connection.displayName,
                        })
                      : t("connect", { provider: connection.displayName })}
                  </Button>
                )}
              </div>
            </article>
          );
        })}
      </div>
    </div>
  );
}
