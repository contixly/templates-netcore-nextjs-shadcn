"use client";

import { useTranslations } from "next-intl";
import {
  useEffect,
  useInsertionEffect,
  useRef,
  useState,
  type FormEvent,
} from "react";

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
import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from "@/src/components/ui/field";
import { Input } from "@/src/components/ui/input";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import {
  createBrowserTeam,
  deleteBrowserTeam,
  updateBrowserTeam,
} from "@/src/lib/api/collaboration/browser/collaboration-mutations";
import type { TeamResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

const supportedTeamName = /^[\p{L}\p{Nd} _-]+$/u;

function useAttachedRef() {
  const attached = useRef(true);
  useInsertionEffect(() => {
    attached.current = true;
    return () => {
      attached.current = false;
    };
  }, []);
  return attached;
}

function useTeamFailureMessage(failure: ApiFailure | null) {
  const t = useTranslations("collaboration.failures");
  if (!failure) return null;
  const codeMessages = new Map<string, string>([
    ["antiforgery_failed", t("codes.antiforgery_failed")],
    ["rate_limited", t("codes.rate_limited")],
    ["team_name_conflict", t("codes.team_name_conflict")],
    ["team_name_unchanged", t("codes.team_name_unchanged")],
    ["team_not_found", t("codes.team_not_found")],
    ["team_permission_denied", t("codes.team_permission_denied")],
    ["validation_failed", t("codes.validation_failed")],
  ]);
  return {
    message:
      failure.kind === "problem" && codeMessages.has(failure.code)
        ? codeMessages.get(failure.code)!
        : t("generic"),
    traceId: failure.kind === "problem" ? failure.traceId : undefined,
  };
}

function FailureNotice({ failure }: Readonly<{ failure: ApiFailure | null }>) {
  const t = useTranslations("collaboration.failures");
  const copy = useTeamFailureMessage(failure);
  return copy ? (
    <Alert variant="destructive">
      <AlertTitle>{copy.message}</AlertTitle>
      {copy.traceId ? (
        <AlertDescription>
          {t("trace", { traceId: copy.traceId })}
        </AlertDescription>
      ) : null}
    </Alert>
  ) : null;
}

function validateName(name: string, message: string): string | null {
  const normalized = name.trim();
  const scalarCount = Array.from(normalized).length;
  return scalarCount === 0 ||
    scalarCount > 50 ||
    !supportedTeamName.test(normalized)
    ? message
    : null;
}

export function TeamCreateForm({
  organizationId,
  onConfirmed,
}: Readonly<{
  organizationId: string;
  onConfirmed: (team: TeamResponse) => void | Promise<void>;
}>) {
  const t = useTranslations("collaboration.teams");
  const attached = useAttachedRef();
  const inFlight = useRef(false);
  const [name, setName] = useState("");
  const [pending, setPending] = useState(false);
  const [validation, setValidation] = useState<string | null>(null);
  const [failure, setFailure] = useState<ApiFailure | null>(null);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (inFlight.current) return;
    const nameError = validateName(name, t("form.nameHint"));
    if (nameError) {
      setValidation(nameError);
      return;
    }
    inFlight.current = true;
    setPending(true);
    setFailure(null);
    const result = await createBrowserTeam(
      createBrowserApiClient(),
      organizationId,
      {
        name: name.trim(),
      },
    );
    if (!attached.current) return;
    inFlight.current = false;
    setPending(false);
    if (!result.ok) {
      setFailure(result.failure);
      return;
    }
    setName("");
    setValidation(null);
    await onConfirmed(result.data);
  }

  return (
    <form className="flex flex-col gap-3" noValidate onSubmit={submit}>
      <Field data-invalid={validation ? true : undefined}>
        <FieldLabel htmlFor="create-team-name">{t("form.name")}</FieldLabel>
        <div className="flex flex-col gap-2 sm:flex-row">
          <Input
            autoComplete="off"
            className="sm:max-w-md"
            disabled={pending}
            id="create-team-name"
            onChange={(event) => {
              setName(event.currentTarget.value);
              setValidation(null);
              setFailure(null);
            }}
            value={name}
          />
          <Button className="sm:shrink-0" disabled={pending} type="submit">
            {pending ? t("actions.creating") : t("actions.create")}
          </Button>
        </div>
        <FieldDescription>{t("form.nameHint")}</FieldDescription>
        <FieldError>{validation}</FieldError>
      </Field>
      <FailureNotice failure={failure} />
    </form>
  );
}

export function TeamRenameForm({
  organizationId,
  team,
  onConfirmed,
}: Readonly<{
  organizationId: string;
  team: TeamResponse;
  onConfirmed: (team: TeamResponse) => void | Promise<void>;
}>) {
  const t = useTranslations("collaboration.teams");
  const attached = useAttachedRef();
  const inFlight = useRef(false);
  const [name, setName] = useState(team.name);
  const [pending, setPending] = useState(false);
  const [validation, setValidation] = useState<string | null>(null);
  const [failure, setFailure] = useState<ApiFailure | null>(null);

  useEffect(() => {
    if (!inFlight.current) {
      setName(team.name);
      setValidation(null);
      setFailure(null);
    }
  }, [team.name]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (inFlight.current) return;
    const nameError = validateName(name, t("form.nameHint"));
    if (nameError) {
      setValidation(nameError);
      return;
    }
    if (name.trim() === team.name) {
      setValidation(t("failures.unchanged"));
      return;
    }
    inFlight.current = true;
    setPending(true);
    setFailure(null);
    const result = await updateBrowserTeam(
      createBrowserApiClient(),
      organizationId,
      team.id,
      { name: name.trim() },
    );
    if (!attached.current) return;
    inFlight.current = false;
    setPending(false);
    if (!result.ok) {
      setFailure(result.failure);
      return;
    }
    setName(result.data.name);
    setValidation(null);
    await onConfirmed(result.data);
  }

  return (
    <form className="flex flex-col gap-3" noValidate onSubmit={submit}>
      <Field data-invalid={validation ? true : undefined}>
        <FieldLabel htmlFor={`rename-team-${team.id}`}>
          {t("form.name")}
        </FieldLabel>
        <div className="flex flex-col gap-2 sm:flex-row">
          <Input
            autoComplete="off"
            className="sm:max-w-md"
            disabled={pending}
            id={`rename-team-${team.id}`}
            onChange={(event) => {
              setName(event.currentTarget.value);
              setValidation(null);
              setFailure(null);
            }}
            value={name}
          />
          <Button
            className="sm:shrink-0"
            disabled={pending}
            type="submit"
            variant="outline"
          >
            {pending ? t("actions.renaming") : t("actions.rename")}
          </Button>
        </div>
        <FieldError>{validation}</FieldError>
      </Field>
      <FailureNotice failure={failure} />
    </form>
  );
}

export function TeamDeleteDialog({
  organizationId,
  team,
  onConfirmed,
}: Readonly<{
  organizationId: string;
  team: TeamResponse;
  onConfirmed: (teamId: string) => void | Promise<void>;
}>) {
  const t = useTranslations("collaboration.teams");
  const attached = useAttachedRef();
  const inFlight = useRef(false);
  const [open, setOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<ApiFailure | null>(null);

  async function confirm() {
    if (inFlight.current) return;
    inFlight.current = true;
    setPending(true);
    setFailure(null);
    const result = await deleteBrowserTeam(
      createBrowserApiClient(),
      organizationId,
      team.id,
    );
    if (!attached.current) return;
    inFlight.current = false;
    setPending(false);
    if (!result.ok || result.data.teamId !== team.id) {
      setFailure(
        result.ok
          ? { kind: "network", code: "api_unavailable" }
          : result.failure,
      );
      return;
    }
    setOpen(false);
    await onConfirmed(team.id);
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => !inFlight.current && setOpen(next)}
    >
      <DialogTrigger asChild>
        <Button
          aria-label={t("actions.deleteNamed", { team: team.name })}
          size="sm"
          type="button"
          variant="destructive"
        >
          {t("actions.delete")}
        </Button>
      </DialogTrigger>
      <DialogContent showCloseButton={false}>
        <DialogHeader>
          <DialogTitle>
            {t("form.deleteTitle", { team: team.name })}
          </DialogTitle>
          <DialogDescription>{t("form.deleteDescription")}</DialogDescription>
        </DialogHeader>
        <FailureNotice failure={failure} />
        <DialogFooter>
          <DialogClose asChild>
            <Button disabled={pending} type="button" variant="outline">
              {t("form.cancel")}
            </Button>
          </DialogClose>
          <Button
            disabled={pending}
            onClick={confirm}
            type="button"
            variant="destructive"
          >
            {pending ? t("actions.deleting") : t("actions.delete")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
