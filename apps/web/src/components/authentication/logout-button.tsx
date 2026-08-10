"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/features/application/ui/interaction-readiness";
import { Button } from "@/src/components/ui/button";
import { authenticationRoutes } from "@/src/features/authentication/authentication-routes";
import { logoutBrowserSession } from "@/src/lib/api/auth/browser/logout-browser-session";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { ApiFailure } from "@/src/lib/api/result";

export function LogoutButton() {
  const router = useRouter();
  const t = useTranslations("auth.logout");
  const interactionReady = useInteractionReady();
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<ApiFailure | null>(null);

  async function executeLogout() {
    setPending(true);
    setFailure(null);
    const result = await logoutBrowserSession(createBrowserApiClient());
    if (!result.ok) {
      setFailure(result.failure);
      setPending(false);
      return;
    }

    router.refresh();
    router.replace(authenticationRoutes.login);
  }

  return (
    <div className="space-y-2">
      {failure ? (
        <div className="text-sm text-destructive" role="alert">
          <p>{t("failure")}</p>
          {failure.kind === "problem" && failure.traceId ? (
            <p className="font-mono text-xs">
              {t("traceId", { traceId: failure.traceId })}
            </p>
          ) : null}
        </div>
      ) : null}
      <Button
        {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
        disabled={!interactionReady || pending}
        onClick={() => void executeLogout()}
        type="button"
        variant="outline"
      >
        {pending ? t("pending") : t("button")}
      </Button>
    </div>
  );
}
