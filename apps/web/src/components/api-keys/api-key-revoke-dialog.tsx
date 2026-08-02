"use client";

import { useState } from "react";
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
  const [open, setOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<ApiFailure | null>(null);

  async function revoke() {
    if (pending) return;
    setPending(true);
    setFailure(null);
    const result = await revokeBrowserApiKey(
      createBrowserApiClient(),
      owner,
      apiKey.id,
    );
    setPending(false);
    if (!result.ok) return setFailure(result.failure);
    onConfirmed(result.data.id);
    setOpen(false);
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !pending && setOpen(next)}>
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
              <p>
                {t(
                  `failures.codes.${
                    failure.kind === "problem" &&
                    [
                      "antiforgery_failed",
                      "api_key_not_found",
                      "api_key_permission_denied",
                      "api_key_update_unchanged",
                      "validation_failed",
                    ].includes(failure.code)
                      ? (failure.code as "api_key_permission_denied")
                      : "generic"
                  }`,
                )}
              </p>
              {failure.kind === "problem" && failure.traceId ? (
                <p className="font-mono">{failure.traceId}</p>
              ) : null}
            </AlertDescription>
          </Alert>
        ) : null}
        <DialogFooter>
          <Button
            disabled={pending}
            onClick={() => setOpen(false)}
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
