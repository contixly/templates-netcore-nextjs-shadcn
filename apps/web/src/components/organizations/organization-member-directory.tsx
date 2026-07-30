"use client";

import { useLocale, useTranslations } from "next-intl";
import { useState } from "react";
import { IconAlertTriangle, IconUsers } from "@tabler/icons-react";

import {
  OrganizationAddMemberDialog,
  type OrganizationRole,
} from "@/src/components/organizations/organization-add-member-dialog";
import { OrganizationMemberRoleControl } from "@/src/components/organizations/organization-member-role-control";
import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import { Badge } from "@/src/components/ui/badge";
import { Button } from "@/src/components/ui/button";
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/src/components/ui/empty";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getOrganizationMembers } from "@/src/lib/api/generated/sdk.gen";
import type {
  OrganizationDetailResponse,
  OrganizationMemberPageResponse,
  OrganizationMemberResponse,
} from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

type ConfirmedAction = "add" | "role";

type RefreshRecovery = Readonly<{
  action: ConfirmedAction;
  traceId?: string;
}>;

type Feedback =
  | Readonly<{ kind: "failure"; message: string; traceId?: string }>
  | Readonly<{ kind: "success"; message: string }>
  | null;

function uniqueMembers(
  members: readonly OrganizationMemberResponse[],
): OrganizationMemberResponse[] {
  const byId = new Map<string, OrganizationMemberResponse>();
  for (const member of members) {
    byId.set(member.id, member);
  }
  return [...byId.values()];
}

function memberDisplayName(member: OrganizationMemberResponse): string {
  return member.name.trim() || member.email;
}

function failureTrace(failure: ApiFailure): string | undefined {
  return failure.kind === "problem" ? failure.traceId : undefined;
}

function assignableRolesFor(
  organization: OrganizationDetailResponse,
): OrganizationRole[] {
  if (
    !organization.capabilities.canAddMembers &&
    !organization.capabilities.canUpdateMemberRoles
  ) {
    return [];
  }
  if (organization.currentRole === "owner") {
    return ["member", "admin", "owner"];
  }
  if (organization.currentRole === "admin") {
    return ["member", "admin"];
  }
  return [];
}

function formattedDate(value: string, locale: string): string {
  const date = new Date(value);
  return Number.isNaN(date.valueOf())
    ? value
    : new Intl.DateTimeFormat(locale, {
        dateStyle: "medium",
        timeZone: "UTC",
      }).format(date);
}

export function OrganizationMemberDirectory({
  currentUserId,
  initialPage,
  organization,
}: Readonly<{
  currentUserId: string;
  initialPage: OrganizationMemberPageResponse;
  organization: OrganizationDetailResponse;
}>) {
  const t = useTranslations("organizations.settings.members");
  const roles = useTranslations("organizations.roles");
  const locale = useLocale();
  const [members, setMembers] = useState(() =>
    uniqueMembers(initialPage.items),
  );
  const [nextCursor, setNextCursor] = useState(initialPage.nextCursor);
  const [pendingRead, setPendingRead] = useState(false);
  const [feedback, setFeedback] = useState<Feedback>(null);
  const [refreshRecovery, setRefreshRecovery] =
    useState<RefreshRecovery | null>(null);
  const currentMember =
    members.find((member) => member.userId === currentUserId) ?? null;
  const otherMembers = members.filter(
    (member) => member.userId !== currentUserId,
  );
  const outsideCount = members.filter(
    (member) => member.isOutsideAllowedEmailDomains,
  ).length;
  const actorAssignableRoles = assignableRolesFor(organization);

  async function readMembers(
    query: { cursor?: string } | undefined,
  ): Promise<
    | { ok: true; data: OrganizationMemberPageResponse }
    | { ok: false; failure: ApiFailure }
  > {
    try {
      const result = await getOrganizationMembers({
        client: createBrowserApiClient(),
        cache: "no-store",
        path: { organizationId: organization.id },
        ...(query ? { query } : {}),
      });
      return result.data === undefined
        ? {
            ok: false,
            failure: normalizeApiFailure(result.error, result.response),
          }
        : { ok: true, data: result.data.data };
    } catch (error) {
      return { ok: false, failure: normalizeApiFailure(error) };
    }
  }

  async function loadMore() {
    if (!nextCursor || pendingRead) {
      return;
    }
    setPendingRead(true);
    setFeedback(null);
    const result = await readMembers({ cursor: nextCursor });
    if (result.ok) {
      setMembers((current) =>
        uniqueMembers([...current, ...result.data.items]),
      );
      setNextCursor(result.data.nextCursor);
    } else {
      setFeedback({
        kind: "failure",
        message: t("loadFailure"),
        traceId: failureTrace(result.failure),
      });
    }
    setPendingRead(false);
  }

  async function refreshAfterMutation(action: ConfirmedAction) {
    setPendingRead(true);
    const result = await readMembers(undefined);
    if (!result.ok) {
      setFeedback(null);
      setRefreshRecovery({
        action,
        traceId: failureTrace(result.failure),
      });
      setPendingRead(false);
      return;
    }

    setMembers((current) => uniqueMembers([...current, ...result.data.items]));
    setNextCursor(result.data.nextCursor);
    setRefreshRecovery(null);
    setFeedback({
      kind: "success",
      message: action === "add" ? t("addSuccess") : t("roleSuccess"),
    });
    setPendingRead(false);
  }

  async function confirmMember(
    member: OrganizationMemberResponse,
    action: ConfirmedAction,
  ) {
    setMembers((current) => {
      const index = current.findIndex(
        (candidate) => candidate.id === member.id,
      );
      if (index < 0) {
        return [...current, member];
      }
      return current.map((candidate) =>
        candidate.id === member.id ? member : candidate,
      );
    });
    setFeedback(null);
    setRefreshRecovery(null);
    await refreshAfterMutation(action);
  }

  function roleOptions(member: OrganizationMemberResponse): OrganizationRole[] {
    if (
      !organization.capabilities.canUpdateMemberRoles ||
      member.userId === currentUserId ||
      (organization.currentRole === "admin" && member.role === "owner")
    ) {
      return [];
    }
    return actorAssignableRoles;
  }

  function memberIdentity(member: OrganizationMemberResponse) {
    return (
      <div className="flex min-w-0 flex-col gap-1">
        <p className="truncate text-sm font-medium">
          {memberDisplayName(member)}
        </p>
        <p className="truncate text-xs text-muted-foreground">{member.email}</p>
        <div className="flex flex-wrap gap-2">
          <Badge variant="outline">{roles(member.role)}</Badge>
          {member.isOutsideAllowedEmailDomains ? (
            <Badge variant="outline">{t("outsidePolicy")}</Badge>
          ) : null}
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      {outsideCount > 0 ? (
        <Alert>
          <IconAlertTriangle aria-hidden="true" />
          <AlertTitle>{t("outsideSummaryTitle")}</AlertTitle>
          <AlertDescription>
            {t("outsideSummaryDescription", { count: outsideCount })}
          </AlertDescription>
        </Alert>
      ) : null}

      {feedback ? (
        <Alert
          role={feedback.kind === "success" ? "status" : undefined}
          variant={feedback.kind === "failure" ? "destructive" : "default"}
        >
          <AlertTitle>{feedback.message}</AlertTitle>
          {feedback.kind === "failure" && feedback.traceId ? (
            <AlertDescription className="font-mono text-xs">
              {feedback.traceId}
            </AlertDescription>
          ) : null}
        </Alert>
      ) : null}

      {refreshRecovery ? (
        <Alert variant="destructive">
          <AlertTitle>{t("refreshFailure")}</AlertTitle>
          <AlertDescription className="flex flex-col items-start gap-2">
            {refreshRecovery.traceId ? (
              <span className="font-mono text-xs">
                {refreshRecovery.traceId}
              </span>
            ) : null}
            <Button
              disabled={pendingRead}
              onClick={() => void refreshAfterMutation(refreshRecovery.action)}
              type="button"
              variant="outline"
            >
              {pendingRead ? t("refreshing") : t("retryRefresh")}
            </Button>
          </AlertDescription>
        </Alert>
      ) : null}

      {currentMember ? (
        <Card
          aria-labelledby="organization-current-member-heading"
          role="region"
        >
          <CardHeader>
            <CardTitle>
              <h2 id="organization-current-member-heading">
                {t("currentTitle")}
              </h2>
            </CardTitle>
            <CardDescription>{t("currentDescription")}</CardDescription>
            <CardAction>
              <Badge variant="secondary">{t("you")}</Badge>
            </CardAction>
          </CardHeader>
          <CardContent className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
            {memberIdentity(currentMember)}
            <p className="text-xs text-muted-foreground">
              {t("joined", {
                date: formattedDate(currentMember.joinedAt, locale),
              })}
            </p>
          </CardContent>
        </Card>
      ) : null}

      <Card aria-labelledby="organization-other-members-heading" role="region">
        <CardHeader>
          <CardTitle>
            <h2 id="organization-other-members-heading">{t("othersTitle")}</h2>
          </CardTitle>
          <CardDescription>{t("othersDescription")}</CardDescription>
          {organization.capabilities.canAddMembers &&
          actorAssignableRoles.length > 0 ? (
            <CardAction>
              <OrganizationAddMemberDialog
                assignableRoles={actorAssignableRoles}
                onMemberConfirmed={(member) => confirmMember(member, "add")}
                organizationId={organization.id}
              />
            </CardAction>
          ) : null}
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {!organization.capabilities.canAddMembers ? (
            <p className="text-sm text-muted-foreground">{t("readOnly")}</p>
          ) : null}
          {otherMembers.length === 0 ? (
            <Empty className="min-h-40 border">
              <EmptyHeader>
                <EmptyMedia variant="icon">
                  <IconUsers />
                </EmptyMedia>
                <EmptyTitle>{t("emptyTitle")}</EmptyTitle>
                <EmptyDescription>{t("emptyDescription")}</EmptyDescription>
              </EmptyHeader>
            </Empty>
          ) : (
            <div className="flex flex-col gap-3">
              {otherMembers.map((member) => {
                const assignableRoles = roleOptions(member);
                return (
                  <article
                    aria-label={t("memberLabel", {
                      name: memberDisplayName(member),
                    })}
                    className="flex flex-col gap-4 border p-4 sm:flex-row sm:items-start sm:justify-between"
                    key={member.id}
                  >
                    <div className="flex min-w-0 flex-1 flex-col gap-3">
                      {memberIdentity(member)}
                      <p className="text-xs text-muted-foreground">
                        {t("joined", {
                          date: formattedDate(member.joinedAt, locale),
                        })}
                      </p>
                    </div>
                    {assignableRoles.length > 0 ? (
                      <OrganizationMemberRoleControl
                        assignableRoles={assignableRoles}
                        member={member}
                        onMemberConfirmed={(confirmed) =>
                          confirmMember(confirmed, "role")
                        }
                        organizationId={organization.id}
                      />
                    ) : null}
                  </article>
                );
              })}
            </div>
          )}
          {nextCursor ? (
            <div className="flex justify-center">
              <Button
                disabled={pendingRead}
                onClick={() => void loadMore()}
                type="button"
                variant="outline"
              >
                {pendingRead ? t("loadingMore") : t("loadMore")}
              </Button>
            </div>
          ) : null}
        </CardContent>
      </Card>
    </div>
  );
}
