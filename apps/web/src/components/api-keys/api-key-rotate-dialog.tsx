"use client";

import { useEffect, useRef, useState, type RefObject } from "react";
import { useTranslations } from "next-intl";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/features/application/ui/interaction-readiness";
import {
  ApiKeySecretView,
  type ApiKeySecretViewHandle,
} from "@/src/components/api-keys/api-key-secret-view";
import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import { Button } from "@/src/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/src/components/ui/dialog";
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
import { rotateBrowserApiKey } from "@/src/lib/api/api-keys/browser/api-key-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { ApiKeyResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

export function ApiKeyRotateDialog({
  apiKey,
  mutationArbiter,
  mutationBusy = false,
  onConfirmed,
  owner,
  secretViewRef,
}: Readonly<{
  apiKey: ApiKeyResponse;
  mutationArbiter?: ApiKeyMutationArbiter;
  mutationBusy?: boolean;
  onConfirmed: (apiKey: ApiKeyResponse) => void;
  owner: ApiKeyOwner;
  secretViewRef?: RefObject<ApiKeySecretViewHandle | null>;
}>) {
  const t = useTranslations("apiKeys");
  const interactionReady = useInteractionReady();
  const localSecretView = useRef<ApiKeySecretViewHandle>(null);
  const secretView = secretViewRef ?? localSecretView;
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
  }

  async function rotate() {
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
      const result = await rotateBrowserApiKey(
        createBrowserApiClient(),
        owner,
        apiKey.id,
      );
      if (
        !mounted.current ||
        generation !== actionGeneration.current ||
        (lease && !mutationArbiter?.isCurrent(lease))
      ) {
        if (result.ok) result.data.key = "";
        return;
      }
      requestInFlight.current = false;
      setPending(false);
      if (!result.ok) return setFailure(result.failure);
      if (result.data.id !== apiKey.id) {
        result.data.key = "";
        setFailure(apiKeyIdentityMismatchFailure());
        return;
      }

      const { key, ...safeApiKey } = result.data;
      setOpen(false);
      secretView.current?.reveal(key);
      onConfirmed(safeApiKey);
    } finally {
      if (lease) mutationArbiter?.release(lease);
    }
  }

  return (
    <>
      <Dialog open={open} onOpenChange={changeOpen}>
        <DialogTrigger asChild>
          <Button
            {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
            disabled={!interactionReady || mutationBusy}
            size="sm"
            type="button"
            variant="outline"
          >
            {t("actions.rotate")}
          </Button>
        </DialogTrigger>
        <DialogContent showCloseButton={false}>
          <DialogHeader>
            <DialogTitle>
              {t("rotate.title", { name: apiKey.name })}
            </DialogTitle>
            <DialogDescription>{t("rotate.description")}</DialogDescription>
          </DialogHeader>
          {failure ? (
            <Alert variant="destructive">
              <AlertTitle>{t("failures.rotate")}</AlertTitle>
              <AlertDescription>
                <p>{t(`failures.codes.${apiKeyFailureMessage(failure)}`)}</p>
                {failure.kind === "problem" && failure.traceId ? (
                  <p className="font-mono">{failure.traceId}</p>
                ) : null}
              </AlertDescription>
            </Alert>
          ) : null}
          <DialogFooter>
            <Button
              disabled={pending}
              onClick={() => changeOpen(false)}
              type="button"
              variant="outline"
            >
              {t("actions.cancel")}
            </Button>
            <Button
              disabled={pending || mutationBusy}
              onClick={() => void rotate()}
              type="button"
              variant="destructive"
            >
              {pending ? t("rotate.submitting") : t("rotate.confirm")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      {secretViewRef ? null : <ApiKeySecretView ref={localSecretView} />}
    </>
  );
}
