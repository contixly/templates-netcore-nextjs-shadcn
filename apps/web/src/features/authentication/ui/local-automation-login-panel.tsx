"use client";

import type { Route } from "next";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { IconRobot } from "@tabler/icons-react";
import { useState } from "react";

import { useInteractionReady } from "@/src/features/application/ui/interaction-readiness";
import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import { LoadingButton } from "@/src/components/ui/custom/button-loading";
import { createLocalAutomationBrowserSession } from "@/src/lib/api/auth/browser/create-local-automation-browser-session";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { ApiFailure } from "@/src/lib/api/result";

function failureKey(failure: ApiFailure) {
  if (failure.kind === "network" || failure.kind === "configuration") {
    return "unavailable" as const;
  }
  return failure.code === "rate_limited"
    ? ("rateLimited" as const)
    : failure.code === "validation_failed" ||
        failure.code === "invalid_request" ||
        failure.code === "antiforgery_failed"
      ? ("invalidRequest" as const)
      : ("failure" as const);
}

export function LocalAutomationLoginPanel({
  redirectPath,
}: Readonly<{ redirectPath: Route }>) {
  const router = useRouter();
  const t = useTranslations("auth.localAutomation");
  const interactionReady = useInteractionReady();
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<ApiFailure | null>(null);

  async function createSession() {
    setPending(true);
    setFailure(null);
    const result = await createLocalAutomationBrowserSession(
      createBrowserApiClient(),
    );
    if (!result.ok) {
      setFailure(result.failure);
      setPending(false);
      return;
    }

    router.refresh();
    router.push(redirectPath);
  }

  return (
    <Card size="sm">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <IconRobot aria-hidden="true" className="size-4" />
          {t("title")}
        </CardTitle>
        <CardDescription>{t("description")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {failure ? (
          <Alert variant="destructive">
            <AlertTitle>{t("failure")}</AlertTitle>
            <AlertDescription>
              <p>{t(failureKey(failure))}</p>
              {failure.kind === "problem" && failure.traceId ? (
                <p className="font-mono text-xs">
                  {t("traceId", { traceId: failure.traceId })}
                </p>
              ) : null}
            </AlertDescription>
          </Alert>
        ) : null}
        <LoadingButton
          className="w-full"
          data-interaction-ready={interactionReady ? "true" : undefined}
          disabled={!interactionReady || pending}
          loading={pending}
          onClick={() => void createSession()}
          type="button"
        >
          <IconRobot aria-hidden="true" data-icon="inline-start" />
          {pending ? t("pending") : t("button")}
        </LoadingButton>
      </CardContent>
    </Card>
  );
}
