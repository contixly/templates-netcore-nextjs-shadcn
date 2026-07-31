"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import {
  useInsertionEffect,
  useLayoutEffect,
  useRef,
  useState,
  type FormEvent,
} from "react";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/components/application/interaction-readiness";
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
  const interactionReady = useInteractionReady();
  const attached = useRef(true);
  const visible = useRef(true);
  const queuedRouterEffects = useRef<(() => void) | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const requestInFlight = useRef(false);
  const [open, setOpen] = useState(false);
  const [confirmation, setConfirmation] = useState("");
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [pending, setPending] = useState(false);
  const matches = confirmation === organization.name;

  useInsertionEffect(() => {
    attached.current = true;
    return () => {
      attached.current = false;
      queuedRouterEffects.current = null;
    };
  }, []);

  useLayoutEffect(() => {
    visible.current = true;
    const routerEffects = queuedRouterEffects.current;
    queuedRouterEffects.current = null;
    routerEffects?.();
    return () => {
      visible.current = false;
    };
  }, []);

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
    if (!attached.current) {
      return;
    }
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
    if (!attached.current) {
      return;
    }
    const completeRouterEffects = () => {
      router.replace(organizationRoutes.workspaces);
      router.refresh();
    };
    if (!visible.current) {
      queuedRouterEffects.current = completeRouterEffects;
      return;
    }
    completeRouterEffects();
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
        <Button
          {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
          disabled={!interactionReady}
          type="button"
          variant="destructive"
        >
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
              {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
              autoComplete="off"
              disabled={!interactionReady || pending}
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
              {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
              disabled={!interactionReady || !matches || pending}
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
