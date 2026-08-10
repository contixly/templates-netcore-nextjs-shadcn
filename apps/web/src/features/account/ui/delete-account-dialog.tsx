"use client";

import { useTranslations } from "next-intl";
import { IconAlertTriangle } from "@tabler/icons-react";
import { useRef, useState, type FormEvent } from "react";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/features/application/ui/interaction-readiness";
import { Button } from "@/src/components/ui/button";
import { LoadingButton } from "@/src/components/ui/custom/button-loading";
import { FormErrorNotice } from "@/src/components/ui/custom/form-error-notice";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/src/components/ui/dialog";
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
} from "@/src/components/ui/field";
import { Input } from "@/src/components/ui/input";
import { deleteBrowserAccount } from "@/src/lib/api/account/browser/account-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { ApiFailure } from "@/src/lib/api/result";

function failureTrace(failure: ApiFailure): string | undefined {
  return failure.kind === "problem" ? failure.traceId : undefined;
}

function isOwnershipTransferRequired(failure: ApiFailure): boolean {
  return (
    failure.kind === "problem" &&
    failure.code === "organization_ownership_transfer_required"
  );
}

export function DeleteAccountDialog({
  primaryEmail,
}: Readonly<{ primaryEmail: string }>) {
  const t = useTranslations("account.deleteAccount");
  const danger = useTranslations("account.danger");
  const interactionReady = useInteractionReady();
  const [open, setOpen] = useState(false);
  const [confirmation, setConfirmation] = useState("");
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [success, setSuccess] = useState(false);
  const [pending, setPending] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const activeRequestRef = useRef<number | null>(null);
  const nextRequestIdRef = useRef(0);
  const matches = confirmation.trim() === primaryEmail;

  function changeOpen(nextOpen: boolean) {
    if (!nextOpen && activeRequestRef.current !== null) {
      return;
    }

    setOpen(nextOpen);
    if (!nextOpen) {
      setConfirmation("");
      setFailure(null);
      setSuccess(false);
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!matches || activeRequestRef.current !== null) {
      return;
    }

    const requestId = ++nextRequestIdRef.current;
    activeRequestRef.current = requestId;
    setPending(true);
    setFailure(null);
    setSuccess(false);
    const result = await deleteBrowserAccount(createBrowserApiClient(), {
      confirmationEmail: confirmation.trim(),
    });

    if (activeRequestRef.current !== requestId) {
      return;
    }

    activeRequestRef.current = null;
    setPending(false);

    if (!result.ok || result.data.deleted !== true) {
      setFailure(
        result.ok
          ? { kind: "network", code: "api_unavailable" }
          : result.failure,
      );
      return;
    }

    setSuccess(true);
    window.location.assign("/");
  }

  return (
    <Dialog onOpenChange={changeOpen} open={open}>
      <DialogTrigger asChild>
        <Button
          {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
          disabled={!interactionReady}
          type="button"
          variant="destructive"
        >
          {danger("open")}
        </Button>
      </DialogTrigger>
      <DialogContent
        className="min-w-md max-sm:min-w-0 sm:max-w-lg"
        onOpenAutoFocus={(event) => {
          event.preventDefault();
          inputRef.current?.focus();
        }}
        onEscapeKeyDown={(event) => {
          if (activeRequestRef.current !== null) {
            event.preventDefault();
          }
        }}
        onInteractOutside={(event) => {
          if (activeRequestRef.current !== null) {
            event.preventDefault();
          }
        }}
        showCloseButton={false}
      >
        <DialogHeader>
          <DialogTitle>{t("title")}</DialogTitle>
          <DialogDescription>{t("description")}</DialogDescription>
        </DialogHeader>

        <div className="rounded-lg border border-destructive/50 bg-destructive/10 p-4">
          <div className="flex gap-3">
            <IconAlertTriangle
              aria-hidden="true"
              className="mt-0.5 size-5 shrink-0 text-destructive"
            />
            <div className="flex flex-col gap-1 text-sm">
              <p className="font-medium text-destructive">
                {danger("warning")}
              </p>
              <p className="text-muted-foreground">{t("description")}</p>
            </div>
          </div>
        </div>

        <form noValidate onSubmit={submit}>
          <FieldGroup>
            <Field data-disabled={!interactionReady || pending}>
              <FieldLabel htmlFor="delete-account-confirmation">
                {t("confirmationLabel", { email: primaryEmail })}
              </FieldLabel>
              <Input
                {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
                aria-describedby="delete-account-confirmation-hint"
                autoComplete="off"
                disabled={!interactionReady || pending}
                id="delete-account-confirmation"
                onChange={(event) => {
                  setConfirmation(event.currentTarget.value);
                  setFailure(null);
                }}
                ref={inputRef}
                value={confirmation}
              />
              <FieldDescription id="delete-account-confirmation-hint">
                {t("confirmationHint")}
              </FieldDescription>
            </Field>

            {failure ? (
              <FormErrorNotice
                title={t(
                  isOwnershipTransferRequired(failure)
                    ? "ownershipTransferRequired"
                    : "failure",
                )}
              >
                {failureTrace(failure) ? (
                  <p className="font-mono text-xs">{failureTrace(failure)}</p>
                ) : null}
              </FormErrorNotice>
            ) : null}
            {success ? (
              <p className="text-sm" role="status">
                {t("success")}
              </p>
            ) : null}

            <DialogFooter>
              <DialogClose asChild>
                <Button disabled={pending} type="button" variant="outline">
                  {t("cancel")}
                </Button>
              </DialogClose>
              <LoadingButton
                {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
                className="text-destructive-foreground bg-destructive hover:bg-destructive/90"
                disabled={!interactionReady || !matches || pending}
                loading={pending}
                type="submit"
                variant="destructive"
              >
                {pending ? t("deleting") : t("confirm")}
              </LoadingButton>
            </DialogFooter>
          </FieldGroup>
        </form>
      </DialogContent>
    </Dialog>
  );
}
