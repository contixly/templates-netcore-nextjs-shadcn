"use client";

import { useTranslations } from "next-intl";
import { useRef, useState, type FormEvent } from "react";
import { IconUserPlus } from "@tabler/icons-react";

import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import { useOrganizationControlInteractionReady } from "@/src/components/organizations/organization-control-readiness";
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
import type {
  OrganizationMemberResponse,
  ProblemDetails,
} from "@/src/lib/api/generated/types.gen";
import { addBrowserOrganizationMember } from "@/src/lib/api/organizations/browser/organization-mutations";
import type { ApiFailure } from "@/src/lib/api/result";

export type OrganizationRole = OrganizationMemberResponse["role"];

const uuid =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

type DomainAcknowledgement = Readonly<{
  userId: string;
  role: OrganizationRole;
  email: string;
  emailDomain: Exclude<ProblemDetails["emailDomain"], undefined>;
  allowedEmailDomains: string[];
}>;

type AddMemberValidation = Readonly<{
  field: "role" | "userId";
  message: string;
}>;

function failureTrace(failure: ApiFailure | null): string | undefined {
  return failure?.kind === "problem" ? failure.traceId : undefined;
}

export function OrganizationAddMemberDialog({
  assignableRoles,
  organizationId,
  onMemberConfirmed,
}: Readonly<{
  assignableRoles: readonly OrganizationRole[];
  organizationId: string;
  onMemberConfirmed: (
    member: OrganizationMemberResponse,
  ) => void | Promise<void>;
}>) {
  const t = useTranslations("organizations.settings.addMemberDialog");
  const roles = useTranslations("organizations.roles");
  const interactionReady = useOrganizationControlInteractionReady();
  const inputRef = useRef<HTMLInputElement>(null);
  const requestInFlight = useRef(false);
  const [open, setOpen] = useState(false);
  const [userId, setUserId] = useState("");
  const [role, setRole] = useState<OrganizationRole>(
    assignableRoles[0] ?? "member",
  );
  const [acknowledgement, setAcknowledgement] =
    useState<DomainAcknowledgement | null>(null);
  const [validation, setValidation] = useState<AddMemberValidation | null>(
    null,
  );
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [pending, setPending] = useState(false);

  if (assignableRoles.length === 0) {
    return null;
  }

  function clearFeedback() {
    setAcknowledgement(null);
    setValidation(null);
    setFailure(null);
  }

  function changeOpen(nextOpen: boolean) {
    if (!nextOpen && requestInFlight.current) {
      return;
    }
    setOpen(nextOpen);
    if (!nextOpen) {
      setUserId("");
      setRole(assignableRoles[0] ?? "member");
      clearFeedback();
    }
  }

  async function add(
    nextUserId: string,
    nextRole: OrganizationRole,
    acknowledgeDomainRestriction = false,
  ) {
    if (requestInFlight.current) {
      return;
    }

    requestInFlight.current = true;
    setPending(true);
    setValidation(null);
    setFailure(null);
    if (acknowledgeDomainRestriction) {
      setAcknowledgement(null);
    }
    const result = await addBrowserOrganizationMember(
      createBrowserApiClient(),
      organizationId,
      {
        userId: nextUserId,
        role: nextRole,
        ...(acknowledgeDomainRestriction
          ? { acknowledgeDomainRestriction: true }
          : {}),
      },
    );

    if (!result.ok) {
      requestInFlight.current = false;
      setPending(false);
      if (
        !acknowledgeDomainRestriction &&
        result.failure.kind === "problem" &&
        result.failure.code === "member_domain_acknowledgement_required" &&
        result.failure.status === 409 &&
        result.failure.email?.trim() &&
        result.failure.emailDomain !== undefined &&
        (result.failure.emailDomain === null ||
          result.failure.emailDomain.trim()) &&
        result.failure.allowedEmailDomains &&
        result.failure.allowedEmailDomains.length > 0 &&
        result.failure.allowedEmailDomains.every((domain) => domain.trim())
      ) {
        setAcknowledgement({
          userId: nextUserId,
          role: nextRole,
          email: result.failure.email,
          emailDomain: result.failure.emailDomain,
          allowedEmailDomains: result.failure.allowedEmailDomains,
        });
        return;
      }

      setFailure(result.failure);
      return;
    }

    setAcknowledgement(null);
    try {
      await onMemberConfirmed(result.data);
      setOpen(false);
      setUserId("");
      setRole(assignableRoles[0] ?? "member");
      clearFeedback();
    } finally {
      requestInFlight.current = false;
      setPending(false);
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const normalizedUserId = userId.trim();
    if (!uuid.test(normalizedUserId)) {
      setValidation({ field: "userId", message: t("userIdInvalid") });
      return;
    }
    if (!assignableRoles.includes(role)) {
      setValidation({ field: "role", message: t("roleInvalid") });
      return;
    }
    await add(normalizedUserId, role);
  }

  const failureMessage =
    failure?.kind === "problem"
      ? ({
          target_user_not_found: t("failures.userNotFound"),
          member_already_exists: t("failures.alreadyMember"),
          role_assignment_forbidden: t("failures.roleForbidden"),
          organization_permission_denied: t("failures.permissionDenied"),
          validation_failed: t("failures.validation"),
          concurrency_conflict: t("failures.concurrency"),
        }[failure.code] ?? t("failures.generic"))
      : t("failures.generic");

  return (
    <Dialog onOpenChange={changeOpen} open={open}>
      <DialogTrigger asChild>
        <Button
          data-organization-control-interaction-ready={
            interactionReady ? "true" : undefined
          }
          disabled={!interactionReady}
          size="sm"
          type="button"
          variant="outline"
        >
          <IconUserPlus data-icon="inline-start" />
          {t("trigger")}
        </Button>
      </DialogTrigger>
      <DialogContent
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
          <FieldGroup>
            <Field
              data-disabled={pending ? true : undefined}
              data-invalid={validation?.field === "userId" ? true : undefined}
            >
              <FieldLabel htmlFor="organization-add-member-user-id">
                {t("userIdLabel")}
              </FieldLabel>
              <Input
                aria-describedby={`organization-add-member-user-id-hint${
                  validation?.field === "userId"
                    ? " organization-add-member-user-id-error"
                    : ""
                }`}
                aria-invalid={validation?.field === "userId" ? true : undefined}
                autoComplete="off"
                disabled={pending}
                id="organization-add-member-user-id"
                onChange={(event) => {
                  setUserId(event.currentTarget.value);
                  clearFeedback();
                }}
                ref={inputRef}
                value={userId}
              />
              <FieldDescription id="organization-add-member-user-id-hint">
                {t("userIdHint")}
              </FieldDescription>
              {validation?.field === "userId" ? (
                <FieldError id="organization-add-member-user-id-error">
                  {validation.message}
                </FieldError>
              ) : null}
            </Field>
            <Field
              data-disabled={pending ? true : undefined}
              data-invalid={validation?.field === "role" ? true : undefined}
            >
              <FieldLabel htmlFor="organization-add-member-role">
                {t("roleLabel")}
              </FieldLabel>
              <Select
                disabled={pending}
                onValueChange={(value) => {
                  if (
                    value === "member" ||
                    value === "admin" ||
                    value === "owner"
                  ) {
                    setRole(value);
                    clearFeedback();
                  }
                }}
                value={role}
              >
                <SelectTrigger
                  aria-describedby={`organization-add-member-role-hint${
                    validation?.field === "role"
                      ? " organization-add-member-role-error"
                      : ""
                  }`}
                  aria-invalid={validation?.field === "role" ? true : undefined}
                  aria-label={t("roleLabel")}
                  className="w-full"
                  id="organization-add-member-role"
                >
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
              <FieldDescription id="organization-add-member-role-hint">
                {t("roleHint")}
              </FieldDescription>
              {validation?.field === "role" ? (
                <FieldError id="organization-add-member-role-error">
                  {validation.message}
                </FieldError>
              ) : null}
            </Field>
          </FieldGroup>

          {acknowledgement ? (
            <Alert>
              <AlertTitle>{t("domainWarningTitle")}</AlertTitle>
              <AlertDescription>
                {t("domainWarningDescription", {
                  email: acknowledgement.email,
                  domain:
                    acknowledgement.emailDomain ?? t("unknownEmailDomain"),
                  domains: acknowledgement.allowedEmailDomains.join(", "),
                })}
              </AlertDescription>
            </Alert>
          ) : null}
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
            {acknowledgement ? (
              <Button
                disabled={pending}
                onClick={() =>
                  void add(acknowledgement.userId, acknowledgement.role, true)
                }
                type="button"
              >
                {pending ? t("adding") : t("confirm")}
              </Button>
            ) : (
              <Button disabled={pending} type="submit">
                {pending ? t("adding") : t("add")}
              </Button>
            )}
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
