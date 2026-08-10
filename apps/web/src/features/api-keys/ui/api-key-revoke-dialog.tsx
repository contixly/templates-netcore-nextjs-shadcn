"use client";

import { useEffect, useRef, useState, type ReactElement } from "react";
import { useTranslations } from "next-intl";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/features/application/ui/interaction-readiness";
import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/src/components/ui/alert-dialog";
import { Button } from "@/src/components/ui/button";
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
import { revokeBrowserApiKey } from "@/src/lib/api/api-keys/browser/api-key-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { ApiKeyResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

export function ApiKeyRevokeDialog({
  apiKey,
  mutationArbiter,
  mutationBusy = false,
  onClosed,
  onConfirmed,
  owner,
  trigger,
}: Readonly<{
  apiKey: ApiKeyResponse;
  mutationArbiter?: ApiKeyMutationArbiter;
  mutationBusy?: boolean;
  onClosed?: () => void;
  onConfirmed: (apiKeyId: string) => void;
  owner: ApiKeyOwner;
  trigger?: ReactElement;
}>) {
  const t = useTranslations("apiKeys");
  const interactionReady = useInteractionReady();
  const mounted = useRef(true);
  const actionGeneration = useRef(0);
  const requestInFlight = useRef(false);
  const [open, setOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<ApiFailure | null>(null);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      actionGeneration.current += 1;
      requestInFlight.current = false;
    };
  }, []);

  function changeOpen(nextOpen: boolean) {
    if (requestInFlight.current) return;
    setOpen(nextOpen);
    if (nextOpen) setFailure(null);
    else onClosed?.();
  }

  async function revoke() {
    if (requestInFlight.current) return;
    const lease: ApiKeyMutationLease | undefined =
      mutationArbiter?.acquire(apiKey.id) ?? undefined;
    if (mutationArbiter && !lease) {
      setFailure(apiKeyMutationBusyFailure());
      return;
    }
    requestInFlight.current = true;
    const generation = ++actionGeneration.current;
    setPending(true);
    setFailure(null);
    try {
      const result = await revokeBrowserApiKey(
        createBrowserApiClient(),
        owner,
        apiKey.id,
      );
      if (
        !mounted.current ||
        generation !== actionGeneration.current ||
        (lease && !mutationArbiter?.isCurrent(lease))
      ) {
        return;
      }
      requestInFlight.current = false;
      setPending(false);
      if (!result.ok) return setFailure(result.failure);
      if (result.data.id !== apiKey.id) {
        setFailure(apiKeyIdentityMismatchFailure());
        return;
      }
      setOpen(false);
      onClosed?.();
      onConfirmed(result.data.id);
    } finally {
      if (lease) mutationArbiter?.release(lease);
    }
  }

  return (
    <AlertDialog open={open} onOpenChange={changeOpen}>
      <AlertDialogTrigger asChild>
        {trigger ?? (
          <Button
            {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
            disabled={!interactionReady || mutationBusy}
            size="sm"
            type="button"
            variant="outline"
          >
            {t("actions.revoke")}
          </Button>
        )}
      </AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            {t("revoke.title", { name: apiKey.name })}
          </AlertDialogTitle>
          <AlertDialogDescription>
            {t("revoke.description")}
          </AlertDialogDescription>
        </AlertDialogHeader>
        {failure ? (
          <Alert variant="destructive">
            <AlertTitle>{t("failures.revoke")}</AlertTitle>
            <AlertDescription>
              <p>{t(`failures.codes.${apiKeyFailureMessage(failure)}`)}</p>
              {failure.kind === "problem" && failure.traceId ? (
                <p className="font-mono">{failure.traceId}</p>
              ) : null}
            </AlertDescription>
          </Alert>
        ) : null}
        <AlertDialogFooter>
          <AlertDialogCancel disabled={pending}>
            {t("actions.cancel")}
          </AlertDialogCancel>
          <Button
            disabled={pending || mutationBusy}
            onClick={() => void revoke()}
            type="button"
            variant="destructive"
          >
            {pending ? t("revoke.submitting") : t("revoke.confirm")}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
