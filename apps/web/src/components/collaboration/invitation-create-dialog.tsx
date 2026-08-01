"use client";

import { useTranslations } from "next-intl";
import { useInsertionEffect, useRef, useState, type FormEvent } from "react";

import {
  InvitationCopyButton,
  invitationAbsoluteUrl,
} from "@/src/components/collaboration/invitation-copy-button";
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
  FieldGroup,
  FieldLabel,
} from "@/src/components/ui/field";
import { Input } from "@/src/components/ui/input";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/src/components/ui/select";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import { createBrowserInvitation } from "@/src/lib/api/collaboration/browser/collaboration-mutations";
import type {
  InvitationResponse,
  OrganizationDetailResponse,
} from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

type InvitationRole = InvitationResponse["role"];
type TeamOption = Readonly<{ id: string; name: string }>;

const noTeamValue = "__no_team__";
const emailPattern = /^[^\s@]+@[^\s@]+$/u;
const printableAsciiPattern = /^[\x20-\x7e]+$/u;

function normalizedEmail(value: string): string | null {
  const email = value.trim().toLowerCase();
  return email.length > 0 &&
    email.length <= 254 &&
    printableAsciiPattern.test(email) &&
    emailPattern.test(email)
    ? email
    : null;
}

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

function hasNotificationFailure(invitation: InvitationResponse): boolean {
  return invitation.warning === "notification_failed";
}

function InvitationFailureNotice({
  failure,
}: Readonly<{ failure: ApiFailure | null }>) {
  const t = useTranslations("collaboration.failures");
  if (!failure) return null;
  const message =
    failure.kind !== "problem"
      ? t("generic")
      : ({
          antiforgery_failed: t("codes.antiforgery_failed"),
          invitation_already_exists: t("codes.invitation_already_exists"),
          invitation_domain_restricted: t("codes.invitation_domain_restricted"),
          invitation_limit_reached: t("codes.invitation_limit_reached"),
          invitation_permission_denied: t("codes.invitation_permission_denied"),
          invitation_recipient_already_member: t(
            "codes.invitation_recipient_already_member",
          ),
          invitation_team_invalid: t("codes.invitation_team_invalid"),
          rate_limited: t("codes.rate_limited"),
          validation_failed: t("codes.validation_failed"),
        }[failure.code] ?? t("generic"));
  return (
    <Alert variant="destructive">
      <AlertTitle>{message}</AlertTitle>
      {failure.kind === "problem" && failure.traceId ? (
        <AlertDescription>
          {t("trace", { traceId: failure.traceId })}
        </AlertDescription>
      ) : null}
    </Alert>
  );
}

export function InvitationCreateDialog({
  currentRole,
  onConfirmed,
  organizationId,
  teams,
}: Readonly<{
  currentRole: OrganizationDetailResponse["currentRole"];
  onConfirmed: (invitation: InvitationResponse) => void | Promise<void>;
  organizationId: string;
  teams: readonly TeamOption[];
}>) {
  const t = useTranslations("collaboration.invitations.create");
  const roles = useTranslations("collaboration.invitations.roles");
  const attached = useAttachedRef();
  const inFlight = useRef(false);
  const assignableRoles: readonly InvitationRole[] =
    currentRole === "owner"
      ? ["member", "admin", "owner"]
      : ["member", "admin"];
  const [open, setOpen] = useState(false);
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<InvitationRole>("member");
  const [teamId, setTeamId] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [validation, setValidation] = useState<string | null>(null);
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [created, setCreated] = useState<InvitationResponse | null>(null);
  const [invalidLink, setInvalidLink] = useState(false);

  function reset() {
    setEmail("");
    setRole("member");
    setTeamId(null);
    setPending(false);
    setValidation(null);
    setFailure(null);
    setCreated(null);
    setInvalidLink(false);
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (inFlight.current) return;
    const normalized = normalizedEmail(email);
    if (!normalized) {
      setValidation(t("emailInvalid"));
      return;
    }
    if (!assignableRoles.includes(role)) {
      setFailure({ kind: "network", code: "api_unavailable" });
      return;
    }
    if (teamId !== null && !teams.some((team) => team.id === teamId)) {
      setFailure({ kind: "network", code: "api_unavailable" });
      return;
    }

    inFlight.current = true;
    setPending(true);
    setFailure(null);
    setInvalidLink(false);
    const result = await createBrowserInvitation(
      createBrowserApiClient(),
      organizationId,
      { email: normalized, role, teamId },
    );
    if (!attached.current) return;
    inFlight.current = false;
    setPending(false);
    if (!result.ok) {
      setFailure(result.failure);
      return;
    }
    const safeLink = invitationAbsoluteUrl(
      result.data.id,
      result.data.invitationPath,
    );
    setCreated(result.data);
    setInvalidLink(!safeLink);
    await onConfirmed(result.data);
  }

  const createdUrl = created
    ? invitationAbsoluteUrl(created.id, created.invitationPath)
    : null;

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (inFlight.current) return;
        setOpen(next);
        if (!next) reset();
      }}
    >
      <DialogTrigger asChild>
        <Button type="button">{t("open")}</Button>
      </DialogTrigger>
      <DialogContent showCloseButton={false}>
        <DialogHeader>
          <DialogTitle>{t("title")}</DialogTitle>
          <DialogDescription>{t("description")}</DialogDescription>
        </DialogHeader>
        {created ? (
          <div className="flex flex-col gap-4">
            <Alert>
              <AlertTitle>{t("created")}</AlertTitle>
            </Alert>
            <Alert>
              <AlertTitle>{t("noEmailWarning")}</AlertTitle>
            </Alert>
            {hasNotificationFailure(created) ? (
              <Alert variant="destructive">
                <AlertTitle>{t("notificationFailedWarning")}</AlertTitle>
              </Alert>
            ) : null}
            {createdUrl && !invalidLink ? (
              <>
                <Field>
                  <FieldLabel htmlFor={`invitation-link-${created.id}`}>
                    {t("link")}
                  </FieldLabel>
                  <Input
                    id={`invitation-link-${created.id}`}
                    readOnly
                    value={createdUrl}
                  />
                </Field>
                <InvitationCopyButton
                  invitationId={created.id}
                  invitationPath={created.invitationPath}
                  onInvalid={() => setInvalidLink(true)}
                />
              </>
            ) : (
              <Alert variant="destructive">
                <AlertTitle>{t("unsafeLinkWarning")}</AlertTitle>
              </Alert>
            )}
            <DialogFooter>
              <DialogClose asChild>
                <Button type="button">{t("close")}</Button>
              </DialogClose>
            </DialogFooter>
          </div>
        ) : (
          <form className="flex flex-col gap-4" noValidate onSubmit={submit}>
            <FieldGroup>
              <Field
                data-disabled={pending ? true : undefined}
                data-invalid={validation ? true : undefined}
              >
                <FieldLabel htmlFor="invitation-email">{t("email")}</FieldLabel>
                <Input
                  aria-invalid={validation ? true : undefined}
                  autoComplete="email"
                  disabled={pending}
                  id="invitation-email"
                  inputMode="email"
                  onChange={(event) => {
                    setEmail(event.currentTarget.value);
                    setValidation(null);
                    setFailure(null);
                  }}
                  type="email"
                  value={email}
                />
                <FieldDescription>{t("emailHint")}</FieldDescription>
                <FieldError>{validation}</FieldError>
              </Field>
              <Field data-disabled={pending ? true : undefined}>
                <FieldLabel htmlFor="invitation-role">{t("role")}</FieldLabel>
                <Select
                  disabled={pending}
                  onValueChange={(value) => {
                    if (assignableRoles.includes(value as InvitationRole)) {
                      setRole(value as InvitationRole);
                    }
                  }}
                  value={role}
                >
                  <SelectTrigger id="invitation-role">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      {assignableRoles.map((assignableRole) => (
                        <SelectItem key={assignableRole} value={assignableRole}>
                          {roles(assignableRole)}
                        </SelectItem>
                      ))}
                    </SelectGroup>
                  </SelectContent>
                </Select>
              </Field>
              <Field
                data-disabled={pending || teams.length === 0 ? true : undefined}
              >
                <FieldLabel htmlFor="invitation-team">{t("team")}</FieldLabel>
                <Select
                  disabled={pending || teams.length === 0}
                  onValueChange={(value) =>
                    setTeamId(value === noTeamValue ? null : value)
                  }
                  value={teamId ?? noTeamValue}
                >
                  <SelectTrigger id="invitation-team">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      <SelectItem value={noTeamValue}>{t("noTeam")}</SelectItem>
                      {teams.map((team) => (
                        <SelectItem key={team.id} value={team.id}>
                          {team.name}
                        </SelectItem>
                      ))}
                    </SelectGroup>
                  </SelectContent>
                </Select>
                <FieldDescription>
                  {teams.length === 0 ? t("noTeamsAvailable") : t("teamHint")}
                </FieldDescription>
              </Field>
            </FieldGroup>
            <InvitationFailureNotice failure={failure} />
            <DialogFooter>
              <DialogClose asChild>
                <Button disabled={pending} type="button" variant="outline">
                  {t("cancel")}
                </Button>
              </DialogClose>
              <Button disabled={pending} type="submit">
                {pending ? t("submitting") : t("submit")}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
