"use client";

import { useTranslations } from "next-intl";
import { useRef, useState } from "react";

import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/src/components/ui/select";
import type { OrganizationRole } from "@/src/components/organizations/organization-add-member-dialog";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { OrganizationMemberResponse } from "@/src/lib/api/generated/types.gen";
import { updateBrowserOrganizationMemberRole } from "@/src/lib/api/organizations/browser/organization-mutations";
import type { ApiFailure } from "@/src/lib/api/result";

function failureTrace(failure: ApiFailure | null): string | undefined {
  return failure?.kind === "problem" ? failure.traceId : undefined;
}

export function OrganizationMemberRoleControl({
  assignableRoles,
  member,
  organizationId,
  onMemberConfirmed,
}: Readonly<{
  assignableRoles: readonly OrganizationRole[];
  member: OrganizationMemberResponse;
  organizationId: string;
  onMemberConfirmed: (
    member: OrganizationMemberResponse,
  ) => void | Promise<void>;
}>) {
  const t = useTranslations("organizations.settings.members");
  const roles = useTranslations("organizations.roles");
  const requestInFlight = useRef(false);
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [pending, setPending] = useState(false);

  async function updateRole(nextRole: OrganizationRole) {
    if (
      nextRole === member.role ||
      !assignableRoles.includes(nextRole) ||
      requestInFlight.current
    ) {
      return;
    }

    requestInFlight.current = true;
    setPending(true);
    setFailure(null);
    const result = await updateBrowserOrganizationMemberRole(
      createBrowserApiClient(),
      organizationId,
      member.id,
      { role: nextRole },
    );
    requestInFlight.current = false;

    if (!result.ok) {
      setPending(false);
      setFailure(result.failure);
      return;
    }

    await onMemberConfirmed(result.data);
    setPending(false);
  }

  const failureMessage =
    failure?.kind === "problem"
      ? ({
          member_role_unchanged: t("roleFailures.unchanged"),
          role_assignment_forbidden: t("roleFailures.forbidden"),
          member_not_found: t("roleFailures.notFound"),
          concurrency_conflict: t("roleFailures.concurrency"),
        }[failure.code] ?? t("roleFailures.generic"))
      : t("roleFailures.generic");

  return (
    <div className="flex flex-col items-start gap-2">
      <Select
        disabled={pending}
        onValueChange={(value) => {
          if (value === "member" || value === "admin" || value === "owner") {
            void updateRole(value);
          }
        }}
        value={member.role}
      >
        <SelectTrigger
          aria-label={t("roleLabel", { name: member.name || member.email })}
          className="w-36"
          size="sm"
        >
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            {assignableRoles.map((role) => (
              <SelectItem key={role} value={role}>
                {roles(role)}
              </SelectItem>
            ))}
          </SelectGroup>
        </SelectContent>
      </Select>
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
    </div>
  );
}
