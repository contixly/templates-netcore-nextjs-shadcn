import { useTranslations } from "next-intl";

import { INTERACTION_READY_ATTRIBUTE } from "@/src/features/application/ui/interaction-readiness";
import { Badge } from "@/src/components/ui/badge";
import { Button } from "@/src/components/ui/button";
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import { Skeleton } from "@/src/components/ui/skeleton";
import type { SystemStatusResponse } from "@/src/lib/api/generated";
import type { SystemStatusSource } from "@/src/lib/api/load-system-status";
import type { ApiFailure } from "@/src/lib/api/result";

export type StatusCardState =
  | { kind: "loading" }
  | { kind: "success"; data: SystemStatusResponse }
  | { kind: "failure"; failure: ApiFailure };

type FailureMessageKey =
  | "errors.configuration"
  | "errors.genericProblem"
  | "errors.internalError"
  | "errors.invalidRequest"
  | "errors.methodNotAllowed"
  | "errors.network"
  | "errors.notFound"
  | "errors.validationFailed";

function failureMessageKey(failure: ApiFailure): FailureMessageKey {
  if (failure.kind === "network") {
    return "errors.network";
  }

  if (failure.kind === "configuration") {
    return "errors.configuration";
  }

  switch (failure.code) {
    case "validation_failed":
      return "errors.validationFailed";
    case "invalid_request":
      return "errors.invalidRequest";
    case "not_found":
      return "errors.notFound";
    case "method_not_allowed":
      return "errors.methodNotAllowed";
    case "internal_error":
      return "errors.internalError";
    default:
      return "errors.genericProblem";
  }
}

export function StatusCard({
  interactionReady = true,
  onRetry,
  source,
  state,
}: Readonly<{
  onRetry?: () => void;
  interactionReady?: boolean;
  source: SystemStatusSource;
  state: StatusCardState;
}>) {
  const t = useTranslations("system.status");
  const actions = useTranslations("common.actions");
  const title = source === "ssr" ? t("ssrTitle") : t("browserTitle");
  const sourceLabel = source === "ssr" ? t("sourceSsr") : t("sourceBrowser");

  return (
    <Card aria-live="polite" data-testid={`status-${source}`} role="status">
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        <CardDescription>
          {t("source")}: {sourceLabel}
        </CardDescription>
        <CardAction>
          <Badge variant="outline">{source.toUpperCase()}</Badge>
        </CardAction>
      </CardHeader>
      <CardContent>
        {state.kind === "loading" ? (
          <p>{t("loading")}</p>
        ) : state.kind === "success" ? (
          <div className="space-y-3">
            <p className="font-medium">{t("success")}</p>
            <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
              <dt className="text-muted-foreground">{t("state")}</dt>
              <dd>{state.data.status}</dd>
              <dt className="text-muted-foreground">{t("apiVersion")}</dt>
              <dd>{state.data.apiVersion}</dd>
              <dt className="text-muted-foreground">{t("timestamp")}</dt>
              <dd>
                <time dateTime={state.data.timestamp}>
                  {state.data.timestamp}
                </time>
              </dd>
              <dt className="text-muted-foreground">{t("echo")}</dt>
              <dd>{state.data.echo ?? "—"}</dd>
            </dl>
          </div>
        ) : (
          <div className="space-y-3">
            <p>{t(failureMessageKey(state.failure))}</p>
            {state.failure.kind === "problem" && state.failure.traceId ? (
              <p className="font-mono text-xs text-muted-foreground">
                {t("traceId", { traceId: state.failure.traceId })}
              </p>
            ) : null}
            {onRetry ? (
              <Button
                {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
                disabled={!interactionReady}
                onClick={onRetry}
                size="sm"
                variant="outline"
              >
                {actions("retry")}
              </Button>
            ) : null}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

export function StatusCardSkeleton({
  label,
  source,
  title,
}: Readonly<{
  label: string;
  source: SystemStatusSource;
  title: string;
}>) {
  return (
    <Card
      aria-label={label}
      aria-live="polite"
      data-testid={`status-${source}`}
      role="status"
    >
      <CardHeader>
        <CardTitle>{title}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <Skeleton className="h-4 w-40" />
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-2/3" />
      </CardContent>
    </Card>
  );
}
