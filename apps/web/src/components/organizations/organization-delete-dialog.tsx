"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useRef, useState, type FormEvent } from "react";

import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import { Button } from "@/src/components/ui/button";
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
import { Field, FieldDescription, FieldLabel } from "@/src/components/ui/field";
import { Input } from "@/src/components/ui/input";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import { deleteBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import type { ApiFailure } from "@/src/lib/api/result";

type DeletableOrganization = Readonly<{ id: string; name: string }>;

function failureTrace(failure: ApiFailure | null): string | undefined {
  return failure?.kind === "problem" ? failure.traceId : undefined;
}

export function OrganizationDeleteDialog({
  canDelete,
  onDeleted,
  organization,
}: Readonly<{
  canDelete: boolean;
  onDeleted?: (organizationId: string) => void | Promise<void>;
  organization: DeletableOrganization;
}>) {
  const t = useTranslations("organizations.settings.deleteDialog");
  const router = useRouter();
  const inputRef = useRef<HTMLInputElement>(null);
  const requestInFlight = useRef(false);
  const [open, setOpen] = useState(false);
  const [confirmation, setConfirmation] = useState("");
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [pending, setPending] = useState(false);
  const matches = confirmation === organization.name;

  if (!canDelete) {
    return null;
  }

  function changeOpen(nextOpen: boolean) {
    if (!nextOpen && requestInFlight.current) {
      return;
    }
    setOpen(nextOpen);
    if (!nextOpen) {
      setConfirmation("");
      setFailure(null);
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!matches || requestInFlight.current) {
      return;
    }

    requestInFlight.current = true;
    setPending(true);
    setFailure(null);
    const result = await deleteBrowserOrganization(
      createBrowserApiClient(),
      organization.id,
      { confirmationName: confirmation },
    );
    requestInFlight.current = false;
    setPending(false);

    if (!result.ok || result.data.organizationId !== organization.id) {
      setFailure(
        result.ok
          ? { kind: "network", code: "api_unavailable" }
          : result.failure,
      );
      return;
    }

    setOpen(false);
    await onDeleted?.(organization.id);
    router.replace(organizationRoutes.workspaces);
    router.refresh();
  }

  const failureMessage =
    failure?.kind === "problem"
      ? ({
          last_organization_required: t("failures.lastOrganization"),
          organization_confirmation_mismatch: t(
            "failures.confirmationMismatch",
          ),
          organization_permission_denied: t("failures.permissionDenied"),
          concurrency_conflict: t("failures.concurrency"),
        }[failure.code] ?? t("failures.generic"))
      : t("failures.generic");

  return (
    <Dialog onOpenChange={changeOpen} open={open}>
      <DialogTrigger asChild>
        <Button type="button" variant="destructive">
          {t("trigger")}
        </Button>
      </DialogTrigger>
      <DialogContent
        className="sm:max-w-lg"
        onEscapeKeyDown={(event) => {
          if (requestInFlight.current) {
            event.preventDefault();
          }
        }}
        onInteractOutside={(event) => {
          if (requestInFlight.current) {
            event.preventDefault();
          }
        }}
        onOpenAutoFocus={(event) => {
          event.preventDefault();
          inputRef.current?.focus();
        }}
        showCloseButton={false}
      >
        <DialogHeader>
          <DialogTitle>{t("title")}</DialogTitle>
          <DialogDescription>{t("description")}</DialogDescription>
        </DialogHeader>
        <form className="flex flex-col gap-4" noValidate onSubmit={submit}>
          <Field data-disabled={pending ? true : undefined}>
            <FieldLabel htmlFor={`delete-organization-${organization.id}`}>
              {t("confirmationLabel", { name: organization.name })}
            </FieldLabel>
            <Input
              autoComplete="off"
              disabled={pending}
              id={`delete-organization-${organization.id}`}
              onChange={(event) => {
                setConfirmation(event.currentTarget.value);
                setFailure(null);
              }}
              ref={inputRef}
              value={confirmation}
            />
            <FieldDescription>{t("confirmationHint")}</FieldDescription>
          </Field>
          {failure ? (
            <Alert variant="destructive">
              <AlertTitle>{failureMessage}</AlertTitle>
              {failureTrace(failure) ? (
                <AlertDescription className="font-mono text-xs">
                  {failureTrace(failure)}
                </AlertDescription>
              ) : null}
            </Alert>
          ) : null}
          <DialogFooter>
            <DialogClose asChild>
              <Button disabled={pending} type="button" variant="outline">
                {t("cancel")}
              </Button>
            </DialogClose>
            <Button
              disabled={!matches || pending}
              type="submit"
              variant="destructive"
            >
              {pending ? t("deleting") : t("confirm")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
