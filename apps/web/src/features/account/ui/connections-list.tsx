"use client";

import { useLocale, useTranslations } from "next-intl";
import { IconLink, IconUnlink } from "@tabler/icons-react";
import { useState } from "react";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/features/application/ui/interaction-readiness";
import { Alert, AlertDescription } from "@/src/components/ui/alert";
import { Badge } from "@/src/components/ui/badge";
import { Button } from "@/src/components/ui/button";
import { FormErrorNotice } from "@/src/components/ui/custom/form-error-notice";
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/src/components/ui/empty";
import {
  Item,
  ItemActions,
  ItemContent,
  ItemDescription,
  ItemGroup,
  ItemMedia,
  ItemTitle,
} from "@/src/components/ui/item";
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
  headingLevel = 2,
  initialConnections,
}: Readonly<{
  headingLevel?: 2 | 3;
  initialConnections: AccountConnectionsResponse;
}>) {
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
  const ConnectionHeading = headingLevel === 3 ? "h3" : "h2";

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
        feedback.kind === "failure" ? (
          <Alert variant="destructive">
            <AlertDescription>
              <p>{feedback.message}</p>
              {feedback.traceId ? (
                <p className="font-mono text-xs">{feedback.traceId}</p>
              ) : null}
            </AlertDescription>
          </Alert>
        ) : (
          <p className="text-sm" role="status">
            {feedback.message}
          </p>
        )
      ) : null}

      {refreshRecovery ? (
        <FormErrorNotice
          title={t("disconnectRefreshFailure", {
            provider: refreshRecovery.provider,
          })}
        >
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
        </FormErrorNotice>
      ) : null}

      {connections.length === 0 ? (
        <Empty className="border">
          <EmptyHeader>
            <EmptyMedia variant="icon">
              <IconLink aria-hidden="true" />
            </EmptyMedia>
            <EmptyTitle>{t("emptyTitle")}</EmptyTitle>
            <EmptyDescription>{t("emptyDescription")}</EmptyDescription>
          </EmptyHeader>
        </Empty>
      ) : (
        <ItemGroup>
          {connections.map((connection) => {
            const pending = pendingProvider === connection.provider;
            const disconnectedReason = connection.disabledReason
              ? t(`disabledReasons.${connection.disabledReason}`)
              : t("disabledReasons.unknown");

            return (
              <Item
                asChild
                className="rounded-lg px-4 py-4 text-sm"
                key={connection.provider}
                variant="outline"
              >
                <article
                  aria-label={t("connectionLabel", {
                    provider: connection.displayName,
                  })}
                >
                  <ItemMedia className="size-10 rounded-full bg-muted">
                    {connection.connected ? (
                      <IconLink aria-hidden="true" />
                    ) : (
                      <IconUnlink aria-hidden="true" />
                    )}
                  </ItemMedia>
                  <ItemContent>
                    <ItemTitle className="flex-wrap text-sm">
                      <ConnectionHeading className="text-sm font-semibold">
                        {connection.displayName}
                      </ConnectionHeading>
                      <Badge
                        variant={connection.connected ? "secondary" : "outline"}
                      >
                        {connection.connected
                          ? t("connected")
                          : t("notConnected")}
                      </Badge>
                      {connection.isCurrentAuthenticationMethod ? (
                        <Badge>{t("currentMethod")}</Badge>
                      ) : null}
                    </ItemTitle>

                    {!connection.configured ? (
                      <ItemDescription>
                        {t("configurationUnavailable")}
                      </ItemDescription>
                    ) : null}
                    {connection.email ? (
                      <ItemDescription className="break-all">
                        {t("email", { email: connection.email })}
                      </ItemDescription>
                    ) : null}
                    {connection.connectedAt ? (
                      <ItemDescription>
                        {t("connectedAt", {
                          date: formattedDate(connection.connectedAt, locale),
                        })}
                      </ItemDescription>
                    ) : null}
                    {connection.connected ? (
                      <ItemDescription>
                        {connection.lastUsedAt
                          ? t("lastUsedAt", {
                              date: formattedDate(
                                connection.lastUsedAt,
                                locale,
                              ),
                            })
                          : t("neverUsed")}
                      </ItemDescription>
                    ) : null}
                    {connection.connected && !connection.canDisconnect ? (
                      <ItemDescription>{disconnectedReason}</ItemDescription>
                    ) : null}
                  </ItemContent>

                  <ItemActions className="ml-auto shrink-0">
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
                        size="sm"
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
                        size="sm"
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
                  </ItemActions>
                </article>
              </Item>
            );
          })}
        </ItemGroup>
      )}
    </div>
  );
}
