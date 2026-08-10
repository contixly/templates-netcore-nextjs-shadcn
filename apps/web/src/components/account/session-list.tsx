"use client";

import { useLocale, useTranslations } from "next-intl";
import { useState } from "react";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/features/application/ui/interaction-readiness";
import { Badge } from "@/src/components/ui/badge";
import { Button } from "@/src/components/ui/button";
import {
  revokeBrowserAccountSession,
  revokeOtherBrowserAccountSessions,
} from "@/src/lib/api/account/browser/account-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import {
  getAccountSessions,
  type AccountSessionResponse,
  type AccountSessionsResponse,
} from "@/src/lib/api/generated";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import type { ApiFailure } from "@/src/lib/api/result";

type Feedback =
  | { kind: "failure"; message: string; traceId?: string }
  | { kind: "success"; message: string }
  | null;

type RevokeOthersRefreshRecovery = Readonly<{
  revokedCount: number;
  traceId?: string;
}>;

type UserAgentPresentation = Readonly<{
  browser: string;
  os: string;
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

function presentUserAgent(
  value: string | null,
  fallback: UserAgentPresentation,
): UserAgentPresentation {
  if (!value) {
    return fallback;
  }

  let browser = fallback.browser;
  if (value.includes("Edg/")) {
    browser = "Edge";
  } else if (value.includes("OPR/") || value.includes("Opera")) {
    browser = "Opera";
  } else if (value.includes("Firefox")) {
    browser = "Firefox";
  } else if (value.includes("Chrome")) {
    browser = "Chrome";
  } else if (value.includes("Safari")) {
    browser = "Safari";
  }

  let os = fallback.os;
  if (value.includes("iPhone") || value.includes("iPad")) {
    os = "iOS";
  } else if (value.includes("Android")) {
    os = "Android";
  } else if (value.includes("Windows")) {
    os = "Windows";
  } else if (value.includes("Mac OS") || value.includes("Macintosh")) {
    os = "macOS";
  } else if (value.includes("Linux")) {
    os = "Linux";
  }

  return { browser, os };
}

export function SessionList({
  headingLevel = 2,
  initialPage,
}: Readonly<{ headingLevel?: 2 | 3; initialPage: AccountSessionsResponse }>) {
  const t = useTranslations("account.sessions");
  const locale = useLocale();
  const interactionReady = useInteractionReady();
  const [sessions, setSessions] = useState(initialPage.items);
  const [nextCursor, setNextCursor] = useState(initialPage.nextCursor);
  const [pendingAction, setPendingAction] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<Feedback>(null);
  const [revokeOthersRefreshRecovery, setRevokeOthersRefreshRecovery] =
    useState<RevokeOthersRefreshRecovery | null>(null);
  const SessionHeading = headingLevel === 3 ? "h3" : "h2";

  async function loadMore() {
    if (!nextCursor || pendingAction) {
      return;
    }

    setPendingAction("load");
    setFeedback(null);
    try {
      const result = await getAccountSessions({
        client: createBrowserApiClient(),
        cache: "no-store",
        query: { cursor: nextCursor },
      });
      if (result.data === undefined) {
        const failure = normalizeApiFailure(result.error, result.response);
        setFeedback({
          kind: "failure",
          message: t("loadFailure"),
          traceId: failureTrace(failure),
        });
        return;
      }

      setSessions((current) => {
        const byId = new Map(current.map((session) => [session.id, session]));
        for (const session of result.data.data.items) {
          byId.set(session.id, session);
        }
        return [...byId.values()];
      });
      setNextCursor(result.data.data.nextCursor);
    } catch (error) {
      const failure = normalizeApiFailure(error);
      setFeedback({
        kind: "failure",
        message: t("loadFailure"),
        traceId: failureTrace(failure),
      });
    } finally {
      setPendingAction(null);
    }
  }

  async function revoke(session: AccountSessionResponse) {
    if (session.isCurrent || pendingAction) {
      return;
    }

    setPendingAction(session.id);
    setFeedback(null);
    const result = await revokeBrowserAccountSession(
      createBrowserApiClient(),
      session.id,
    );
    setPendingAction(null);

    if (!result.ok) {
      setFeedback({
        kind: "failure",
        message: t("revokeFailure"),
        traceId: failureTrace(result.failure),
      });
      return;
    }

    setSessions((current) =>
      current.filter((item) => item.id !== result.data.sessionId),
    );
    setFeedback({ kind: "success", message: t("revokeSuccess") });
  }

  async function revokeOthers() {
    if (pendingAction) {
      return;
    }

    setPendingAction("others");
    setFeedback(null);
    setRevokeOthersRefreshRecovery(null);
    const result = await revokeOtherBrowserAccountSessions(
      createBrowserApiClient(),
    );

    if (!result.ok) {
      setPendingAction(null);
      setFeedback({
        kind: "failure",
        message: t("revokeOthersFailure"),
        traceId: failureTrace(result.failure),
      });
      return;
    }

    setSessions((current) => current.filter((session) => session.isCurrent));
    setNextCursor(null);
    await refreshAfterRevokeOthers(result.data.revokedCount);
  }

  async function refreshAfterRevokeOthers(revokedCount: number) {
    setPendingAction("refresh-others");
    try {
      const refreshed = await getAccountSessions({
        client: createBrowserApiClient(),
        cache: "no-store",
      });
      if (refreshed.data === undefined) {
        const failure = normalizeApiFailure(
          refreshed.error,
          refreshed.response,
        );
        setFeedback(null);
        setRevokeOthersRefreshRecovery({
          revokedCount,
          traceId: failureTrace(failure),
        });
        return;
      }

      setSessions(refreshed.data.data.items);
      setNextCursor(refreshed.data.data.nextCursor);
      setRevokeOthersRefreshRecovery(null);
      setFeedback({
        kind: "success",
        message: t("revokeOthersSuccess", {
          count: revokedCount,
        }),
      });
    } catch (error) {
      const failure = normalizeApiFailure(error);
      setFeedback(null);
      setRevokeOthersRefreshRecovery({
        revokedCount,
        traceId: failureTrace(failure),
      });
    } finally {
      setPendingAction(null);
    }
  }

  const hasOtherSession = sessions.some((session) => !session.isCurrent);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p className="text-xs text-muted-foreground">{t("listLabel")}</p>
        {hasOtherSession ? (
          <Button
            {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
            disabled={!interactionReady || pendingAction !== null}
            onClick={() => void revokeOthers()}
            type="button"
            variant="outline"
          >
            {pendingAction === "others"
              ? t("revokingOthers")
              : t("revokeOthers")}
          </Button>
        ) : null}
      </div>

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

      {revokeOthersRefreshRecovery ? (
        <div
          className="flex flex-col items-start gap-2 text-sm text-destructive"
          role="alert"
        >
          <p>{t("revokeOthersRefreshFailure")}</p>
          {revokeOthersRefreshRecovery.traceId ? (
            <p className="font-mono text-xs">
              {revokeOthersRefreshRecovery.traceId}
            </p>
          ) : null}
          <Button
            {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
            disabled={!interactionReady || pendingAction !== null}
            onClick={() =>
              void refreshAfterRevokeOthers(
                revokeOthersRefreshRecovery.revokedCount,
              )
            }
            type="button"
            variant="outline"
          >
            {pendingAction === "refresh-others"
              ? t("refreshing")
              : t("retryRefresh")}
          </Button>
        </div>
      ) : null}

      {sessions.length === 0 &&
      !revokeOthersRefreshRecovery &&
      pendingAction !== "refresh-others" ? (
        <p className="text-sm text-muted-foreground">{t("empty")}</p>
      ) : sessions.length > 0 ? (
        <div className="grid gap-3">
          {sessions.map((session) => {
            const presentation = presentUserAgent(session.userAgent, {
              browser: t("unknownBrowser"),
              os: t("unknownOs"),
            });
            const title = t("browserOnOs", presentation);
            const articleLabel = session.isCurrent
              ? `${title}, ${t("current")}`
              : title;

            return (
              <article
                aria-label={articleLabel}
                className="flex flex-col gap-4 border p-4 sm:flex-row sm:items-start sm:justify-between"
                key={session.id}
              >
                <div className="flex min-w-0 flex-col gap-2">
                  <div className="flex flex-wrap items-center gap-2">
                    <SessionHeading className="text-sm font-semibold">
                      {title}
                    </SessionHeading>
                    {session.isCurrent ? <Badge>{t("current")}</Badge> : null}
                  </div>
                  <p className="text-xs text-muted-foreground">
                    {t("signedInWith", {
                      method: t(
                        `authenticationMethods.${session.authenticationMethod}`,
                      ),
                    })}
                  </p>
                  {session.ipAddress ? (
                    <p className="text-xs text-muted-foreground">
                      {t("ipAddress", { address: session.ipAddress })}
                    </p>
                  ) : null}
                  <dl className="grid gap-1 text-xs text-muted-foreground sm:grid-cols-3 sm:gap-3">
                    <div>
                      <dt className="sr-only">{t("created", { date: "" })}</dt>
                      <dd>
                        <time dateTime={session.createdAt}>
                          {t("created", {
                            date: formattedDate(session.createdAt, locale),
                          })}
                        </time>
                      </dd>
                    </div>
                    <div>
                      <dt className="sr-only">
                        {t("lastActive", { date: "" })}
                      </dt>
                      <dd>
                        <time dateTime={session.lastSeenAt}>
                          {t("lastActive", {
                            date: formattedDate(session.lastSeenAt, locale),
                          })}
                        </time>
                      </dd>
                    </div>
                    <div>
                      <dt className="sr-only">{t("expires", { date: "" })}</dt>
                      <dd>
                        <time dateTime={session.expiresAt}>
                          {t("expires", {
                            date: formattedDate(session.expiresAt, locale),
                          })}
                        </time>
                      </dd>
                    </div>
                  </dl>
                </div>

                {!session.isCurrent ? (
                  <Button
                    {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
                    aria-label={t("revoke")}
                    disabled={!interactionReady || pendingAction !== null}
                    onClick={() => void revoke(session)}
                    type="button"
                    variant="outline"
                  >
                    {pendingAction === session.id ? t("revoking") : t("revoke")}
                  </Button>
                ) : null}
              </article>
            );
          })}
        </div>
      ) : null}

      {nextCursor ? (
        <Button
          {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
          disabled={!interactionReady || pendingAction !== null}
          onClick={() => void loadMore()}
          type="button"
          variant="outline"
        >
          {pendingAction === "load" ? t("loadingMore") : t("loadMore")}
        </Button>
      ) : null}
    </div>
  );
}
