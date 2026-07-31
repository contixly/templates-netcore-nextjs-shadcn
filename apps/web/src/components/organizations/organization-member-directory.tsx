"use client";

import { useLocale, useTranslations } from "next-intl";
import { useEffect, useReducer, useRef } from "react";
import { IconAlertTriangle, IconUsers } from "@tabler/icons-react";

import {
  OrganizationAddMemberDialog,
  type OrganizationRole,
} from "@/src/components/organizations/organization-add-member-dialog";
import { useOrganizationControlInteractionReady } from "@/src/components/organizations/organization-control-readiness";
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
  OrganizationMemberPageResponse,
  OrganizationMemberResponse,
} from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

export type OrganizationMemberView = Pick<
  OrganizationMemberResponse,
  | "email"
  | "id"
  | "isOutsideAllowedEmailDomains"
  | "joinedAt"
  | "name"
  | "role"
  | "userId"
>;

export type OrganizationMemberPageView = Readonly<{
  items: readonly OrganizationMemberView[];
  nextCursor: string | null;
}>;

export type OrganizationCurrentActorView = Readonly<{
  userId: string;
  name: string;
  email: string;
  role: OrganizationRole;
  isOutsideAllowedEmailDomains: boolean;
}>;

export type OrganizationMemberDirectoryView = Readonly<{
  id: string;
  currentRole: OrganizationRole;
  capabilities: Readonly<{
    canAddMembers: boolean;
    canUpdateMemberRoles: boolean;
  }>;
}>;

type ConfirmedAction = "add" | "role";
type ReadKind = "loadMore" | "refresh";

type RefreshRecovery = Readonly<{
  action: ConfirmedAction;
  traceId?: string;
}>;

type Feedback =
  | Readonly<{ kind: "failure"; message: string; traceId?: string }>
  | Readonly<{ kind: "success"; message: string }>
  | null;

type ActiveRead = Readonly<{
  id: number;
  kind: ReadKind;
  action?: ConfirmedAction;
}>;

type ConfirmedOverlay = Readonly<{
  member: OrganizationMemberView;
  confirmedAfterReadId: number;
}>;

type DirectoryState = Readonly<{
  pages: readonly OrganizationMemberPageView[];
  serverPage: OrganizationMemberPageView;
  confirmedById: ReadonlyMap<string, ConfirmedOverlay>;
  confirmedOrder: readonly string[];
  activeRead: ActiveRead | null;
  feedback: Feedback;
  refreshRecovery: RefreshRecovery | null;
}>;

type DirectoryAction =
  | Readonly<{ type: "serverReconciled"; page: OrganizationMemberPageView }>
  | Readonly<{
      type: "confirm";
      member: OrganizationMemberView;
      confirmedAfterReadId: number;
    }>
  | Readonly<{ type: "readStarted"; read: ActiveRead }>
  | Readonly<{
      type: "loadMoreSucceeded";
      readId: number;
      page: OrganizationMemberPageView;
    }>
  | Readonly<{
      type: "refreshSucceeded";
      readId: number;
      page: OrganizationMemberPageView;
      action: ConfirmedAction;
      successMessage: string;
    }>
  | Readonly<{
      type: "loadMoreFailed";
      readId: number;
      message: string;
      traceId?: string;
    }>
  | Readonly<{
      type: "refreshFailed";
      readId: number;
      action: ConfirmedAction;
      traceId?: string;
    }>;

type ReadCoordinator = Readonly<{
  id: number;
  controller: AbortController;
  superseded: Promise<void>;
  settleSuperseded: () => void;
}>;

function memberView(
  member: OrganizationMemberResponse,
): OrganizationMemberView {
  return {
    id: member.id,
    userId: member.userId,
    name: member.name,
    email: member.email,
    role: member.role,
    joinedAt: member.joinedAt,
    isOutsideAllowedEmailDomains: member.isOutsideAllowedEmailDomains,
  };
}

function pageView(
  page: OrganizationMemberPageResponse,
): OrganizationMemberPageView {
  return {
    items: page.items.map(memberView),
    nextCursor: page.nextCursor,
  };
}

function replaceFirstPage(
  pages: readonly OrganizationMemberPageView[],
  incoming: OrganizationMemberPageView,
): readonly OrganizationMemberPageView[] {
  return pages.length === 0 ? [incoming] : [incoming, ...pages.slice(1)];
}

function reconcileConfirmedOverlays(
  state: DirectoryState,
  page: OrganizationMemberPageView,
  readId: number,
): Readonly<{
  confirmedById: ReadonlyMap<string, ConfirmedOverlay>;
  pages: readonly OrganizationMemberPageView[];
}> {
  const authoritativeById = new Map(
    page.items.map((member) => [member.id, member] as const),
  );
  const confirmedById = new Map(state.confirmedById);
  const retiredById = new Map<string, OrganizationMemberView>();

  for (const [memberId, overlay] of confirmedById) {
    const authoritative = authoritativeById.get(memberId);
    if (authoritative && readId > overlay.confirmedAfterReadId) {
      confirmedById.delete(memberId);
      retiredById.set(memberId, authoritative);
    }
  }

  if (retiredById.size === 0) {
    return { confirmedById, pages: state.pages };
  }

  return {
    confirmedById,
    pages: state.pages.map((existingPage) => ({
      ...existingPage,
      items: existingPage.items.map(
        (member) => retiredById.get(member.id) ?? member,
      ),
    })),
  };
}

function directoryReducer(
  state: DirectoryState,
  action: DirectoryAction,
): DirectoryState {
  if (action.type === "serverReconciled") {
    return {
      ...state,
      pages: replaceFirstPage(state.pages, action.page),
      serverPage: action.page,
    };
  }

  if (action.type === "confirm") {
    const confirmedById = new Map(state.confirmedById);
    confirmedById.set(action.member.id, {
      member: action.member,
      confirmedAfterReadId: action.confirmedAfterReadId,
    });
    return {
      ...state,
      confirmedById,
      confirmedOrder: state.confirmedOrder.includes(action.member.id)
        ? state.confirmedOrder
        : [...state.confirmedOrder, action.member.id],
      feedback: null,
      refreshRecovery: null,
    };
  }

  if (action.type === "readStarted") {
    return {
      ...state,
      activeRead: action.read,
      ...(action.read.kind === "loadMore" ? { feedback: null } : {}),
    };
  }

  if (state.activeRead?.id !== action.readId) {
    return state;
  }

  if (action.type === "loadMoreSucceeded") {
    const reconciled = reconcileConfirmedOverlays(
      state,
      action.page,
      action.readId,
    );
    return {
      ...state,
      pages: [...reconciled.pages, action.page],
      confirmedById: reconciled.confirmedById,
      activeRead: null,
    };
  }

  if (action.type === "refreshSucceeded") {
    const reconciled = reconcileConfirmedOverlays(
      state,
      action.page,
      action.readId,
    );
    return {
      ...state,
      pages: replaceFirstPage(reconciled.pages, action.page),
      confirmedById: reconciled.confirmedById,
      activeRead: null,
      refreshRecovery: null,
      feedback: { kind: "success", message: action.successMessage },
    };
  }

  if (action.type === "loadMoreFailed") {
    return {
      ...state,
      activeRead: null,
      feedback: {
        kind: "failure",
        message: action.message,
        traceId: action.traceId,
      },
    };
  }

  return {
    ...state,
    activeRead: null,
    feedback: null,
    refreshRecovery: {
      action: action.action,
      traceId: action.traceId,
    },
  };
}

function orderedVisibleMembers(
  state: DirectoryState,
): OrganizationMemberView[] {
  const orderedServerMembers: OrganizationMemberView[] = [];
  const serverIds = new Set<string>();
  for (const page of state.pages) {
    for (const member of page.items) {
      if (!serverIds.has(member.id)) {
        serverIds.add(member.id);
        orderedServerMembers.push(
          state.confirmedById.get(member.id)?.member ?? member,
        );
      }
    }
  }

  for (const memberId of state.confirmedOrder) {
    const confirmed = state.confirmedById.get(memberId);
    if (confirmed && !serverIds.has(memberId)) {
      orderedServerMembers.push(confirmed.member);
    }
  }
  return orderedServerMembers;
}

function memberDisplayName(
  member: Pick<OrganizationMemberView, "email" | "name">,
) {
  return member.name.trim() || member.email;
}

function failureTrace(failure: ApiFailure): string | undefined {
  return failure.kind === "problem" ? failure.traceId : undefined;
}

function assignableRolesFor(
  organization: OrganizationMemberDirectoryView,
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

function createReadCoordinator(id: number): ReadCoordinator {
  let settleSuperseded = () => {};
  const superseded = new Promise<void>((resolve) => {
    settleSuperseded = resolve;
  });
  return {
    id,
    controller: new AbortController(),
    superseded,
    settleSuperseded,
  };
}

export function OrganizationMemberDirectory({
  currentActor,
  initialPage,
  organization,
}: Readonly<{
  currentActor: OrganizationCurrentActorView;
  initialPage: OrganizationMemberPageView;
  organization: OrganizationMemberDirectoryView;
}>) {
  const t = useTranslations("organizations.settings.members");
  const roles = useTranslations("organizations.roles");
  const locale = useLocale();
  const interactionReady = useOrganizationControlInteractionReady();
  const [state, dispatch] = useReducer(directoryReducer, {
    pages: [initialPage],
    serverPage: initialPage,
    confirmedById: new Map<string, ConfirmedOverlay>(),
    confirmedOrder: [],
    activeRead: null,
    feedback: null,
    refreshRecovery: null,
  });
  const readGeneration = useRef(0);
  const activeReadCoordinator = useRef<ReadCoordinator | null>(null);
  const serverPageChanged = state.serverPage !== initialPage;
  const visibleState = serverPageChanged
    ? { ...state, pages: replaceFirstPage(state.pages, initialPage) }
    : state;
  const members = orderedVisibleMembers(visibleState);
  const otherMembers = members.filter(
    (member) => member.userId !== currentActor.userId,
  );
  const outsideCount =
    otherMembers.filter((member) => member.isOutsideAllowedEmailDomains)
      .length + (currentActor.isOutsideAllowedEmailDomains ? 1 : 0);
  const actorAssignableRoles = assignableRolesFor(organization);
  const nextCursor = visibleState.pages.at(-1)?.nextCursor ?? null;
  const pendingRead = state.activeRead !== null;

  useEffect(() => {
    if (state.serverPage === initialPage) {
      return;
    }
    dispatch({ type: "serverReconciled", page: initialPage });
  }, [initialPage, state.serverPage]);

  useEffect(
    () => () => {
      const activeRead = activeReadCoordinator.current;
      activeReadCoordinator.current = null;
      activeRead?.controller.abort();
      activeRead?.settleSuperseded();
    },
    [],
  );

  function startRead(
    kind: ReadKind,
    action?: ConfirmedAction,
  ): ReadCoordinator {
    const supersededRead = activeReadCoordinator.current;
    supersededRead?.controller.abort();
    supersededRead?.settleSuperseded();

    const read = createReadCoordinator(++readGeneration.current);
    activeReadCoordinator.current = read;
    dispatch({
      type: "readStarted",
      read: { id: read.id, kind, ...(action ? { action } : {}) },
    });
    return read;
  }

  async function readMembers(
    query: { cursor?: string } | undefined,
    signal: AbortSignal,
  ): Promise<
    | { ok: true; data: OrganizationMemberPageView }
    | { ok: false; failure: ApiFailure }
  > {
    try {
      const result = await getOrganizationMembers({
        client: createBrowserApiClient(),
        cache: "no-store",
        path: { organizationId: organization.id },
        signal,
        ...(query ? { query } : {}),
      });
      return result.data === undefined
        ? {
            ok: false,
            failure: normalizeApiFailure(result.error, result.response),
          }
        : { ok: true, data: pageView(result.data.data) };
    } catch (error) {
      return { ok: false, failure: normalizeApiFailure(error) };
    }
  }

  async function finishRead(
    read: ReadCoordinator,
    query: { cursor?: string } | undefined,
  ): Promise<
    | { ok: true; data: OrganizationMemberPageView }
    | { ok: false; failure: ApiFailure }
    | null
  > {
    const outcome = await Promise.race([
      readMembers(query, read.controller.signal).then((result) => ({
        kind: "completed" as const,
        result,
      })),
      read.superseded.then(() => ({ kind: "superseded" as const })),
    ]);
    if (
      outcome.kind === "superseded" ||
      activeReadCoordinator.current !== read
    ) {
      return null;
    }
    activeReadCoordinator.current = null;
    return outcome.result;
  }

  async function loadMore() {
    if (
      !interactionReady ||
      !nextCursor ||
      activeReadCoordinator.current !== null
    ) {
      return;
    }
    const read = startRead("loadMore");
    const result = await finishRead(read, { cursor: nextCursor });
    if (!result) {
      return;
    }
    if (result.ok) {
      dispatch({
        type: "loadMoreSucceeded",
        readId: read.id,
        page: result.data,
      });
    } else {
      dispatch({
        type: "loadMoreFailed",
        readId: read.id,
        message: t("loadFailure"),
        traceId: failureTrace(result.failure),
      });
    }
  }

  async function refreshAfterMutation(action: ConfirmedAction) {
    const read = startRead("refresh", action);
    const result = await finishRead(read, undefined);
    if (!result) {
      return;
    }
    if (!result.ok) {
      dispatch({
        type: "refreshFailed",
        readId: read.id,
        action,
        traceId: failureTrace(result.failure),
      });
      return;
    }

    dispatch({
      type: "refreshSucceeded",
      readId: read.id,
      page: result.data,
      action,
      successMessage: action === "add" ? t("addSuccess") : t("roleSuccess"),
    });
  }

  async function confirmMember(
    member: OrganizationMemberResponse,
    action: ConfirmedAction,
  ) {
    dispatch({
      type: "confirm",
      member: memberView(member),
      confirmedAfterReadId: readGeneration.current,
    });
    await refreshAfterMutation(action);
  }

  function roleOptions(member: OrganizationMemberView): OrganizationRole[] {
    if (
      !organization.capabilities.canUpdateMemberRoles ||
      member.userId === currentActor.userId ||
      (organization.currentRole === "admin" && member.role === "owner")
    ) {
      return [];
    }
    return actorAssignableRoles;
  }

  function memberIdentity(
    member: Pick<
      OrganizationMemberView,
      "email" | "isOutsideAllowedEmailDomains" | "name" | "role"
    >,
  ) {
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

      {state.feedback ? (
        <Alert
          role={state.feedback.kind === "success" ? "status" : undefined}
          variant={
            state.feedback.kind === "failure" ? "destructive" : "default"
          }
        >
          <AlertTitle>{state.feedback.message}</AlertTitle>
          {state.feedback.kind === "failure" && state.feedback.traceId ? (
            <AlertDescription className="font-mono text-xs">
              {state.feedback.traceId}
            </AlertDescription>
          ) : null}
        </Alert>
      ) : null}

      {state.refreshRecovery ? (
        <Alert variant="destructive">
          <AlertTitle>{t("refreshFailure")}</AlertTitle>
          <AlertDescription className="flex flex-col items-start gap-2">
            {state.refreshRecovery.traceId ? (
              <span className="font-mono text-xs">
                {state.refreshRecovery.traceId}
              </span>
            ) : null}
            <Button
              disabled={pendingRead}
              onClick={() =>
                void refreshAfterMutation(state.refreshRecovery!.action)
              }
              type="button"
              variant="outline"
            >
              {pendingRead ? t("refreshing") : t("retryRefresh")}
            </Button>
          </AlertDescription>
        </Alert>
      ) : null}

      <Card aria-labelledby="organization-current-member-heading" role="region">
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
        <CardContent>{memberIdentity(currentActor)}</CardContent>
      </Card>

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
                data-organization-control-interaction-ready={
                  interactionReady ? "true" : undefined
                }
                disabled={!interactionReady || pendingRead}
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
