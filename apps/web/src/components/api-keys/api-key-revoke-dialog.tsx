"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/components/application/interaction-readiness";
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
} from "@/src/features/api-keys/api-key-failures";
import type { ApiKeyOwner } from "@/src/features/api-keys/api-key-routes";
import { revokeBrowserApiKey } from "@/src/lib/api/api-keys/browser/api-key-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { ApiKeyResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

export function ApiKeyRevokeDialog({
  apiKey,
  onConfirmed,
  owner,
}: Readonly<{
  apiKey: ApiKeyResponse;
  onConfirmed: (apiKeyId: string) => void;
  owner: ApiKeyOwner;
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
  }

  async function revoke() {
    if (requestInFlight.current) return;
    requestInFlight.current = true;
    const generation = ++actionGeneration.current;
    setPending(true);
    setFailure(null);
    const result = await revokeBrowserApiKey(
      createBrowserApiClient(),
      owner,
      apiKey.id,
    );
    if (!mounted.current || generation !== actionGeneration.current) return;
    requestInFlight.current = false;
    setPending(false);
    if (!result.ok) return setFailure(result.failure);
    if (result.data.id !== apiKey.id) {
      setFailure(apiKeyIdentityMismatchFailure());
      return;
    }
    setOpen(false);
    onConfirmed(result.data.id);
  }

  return (
    <Dialog open={open} onOpenChange={changeOpen}>
      <DialogTrigger asChild>
        <Button
          {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
          disabled={!interactionReady}
          size="sm"
          type="button"
          variant="outline"
        >
          {t("actions.revoke")}
        </Button>
      </DialogTrigger>
      <DialogContent showCloseButton={false}>
        <DialogHeader>
          <DialogTitle>{t("revoke.title", { name: apiKey.name })}</DialogTitle>
          <DialogDescription>{t("revoke.description")}</DialogDescription>
        </DialogHeader>
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
            disabled={pending}
            onClick={() => void revoke()}
            type="button"
            variant="destructive"
          >
            {pending ? t("revoke.submitting") : t("revoke.confirm")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
