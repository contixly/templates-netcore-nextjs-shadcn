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
import { IconUsers } from "@tabler/icons-react";

import {
  TeamCreateDialog,
  TeamDeleteDialog,
  TeamRenameDialog,
} from "@/src/features/collaboration/ui/team-controls";
import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import { Avatar, AvatarFallback } from "@/src/components/ui/avatar";
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
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/src/components/ui/empty";
import { Field, FieldLabel } from "@/src/components/ui/field";
import { Input } from "@/src/components/ui/input";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/src/components/ui/table";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import {
  addBrowserTeamMember,
  removeBrowserTeamMember,
} from "@/src/lib/api/collaboration/browser/collaboration-mutations";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import {
  getTeamMemberCandidates,
  getTeamMembers,
  getTeams,
} from "@/src/lib/api/generated/sdk.gen";
import type {
  TeamCandidateResponse,
  TeamMemberPageResponse,
  TeamMemberResponse,
  TeamPageResponse,
  TeamResponse,
} from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

export type TeamDirectoryPage = Readonly<{
  items: readonly TeamResponse[];
  nextCursor: string | null;
}>;

type OrganizationView = Readonly<{
  id: string;
  canManageTeams: boolean;
}>;

type ConfirmedMemberMutation = Readonly<{
  action: "added" | "removed";
  member: TeamMemberResponse;
  confirmedAfterReadGeneration: number;
}>;

type ConfirmedMemberCount = Readonly<{
  count: number;
  confirmedAfterReadGeneration: number;
}>;

type ConfirmedTeamMutation =
  | Readonly<{
      action: "created" | "renamed";
      team: TeamResponse;
      confirmedAfterReadGeneration: number;
    }>
  | Readonly<{
      action: "deleted";
      teamId: string;
      confirmedAfterReadGeneration: number;
    }>;

function mergeUniqueById<T>(
  current: readonly T[],
  incoming: readonly T[],
  identify: (item: T) => string,
): readonly T[] {
  const merged: T[] = [];
  const indexById = new Map<string, number>();

  for (const item of [...current, ...incoming]) {
    const id = identify(item);
    const existingIndex = indexById.get(id);
    if (existingIndex === undefined) {
      indexById.set(id, merged.length);
      merged.push(item);
    } else {
      merged[existingIndex] = item;
    }
  }

  return merged;
}

function withConfirmedMemberMutations(
  rawMembers: readonly TeamMemberResponse[],
  confirmed: ReadonlyMap<string, ConfirmedMemberMutation>,
): readonly TeamMemberResponse[] {
  const visible: TeamMemberResponse[] = [];
  const returnedUserIds = new Set<string>();
  for (const member of rawMembers) {
    if (returnedUserIds.has(member.userId)) continue;
    returnedUserIds.add(member.userId);
    const overlay = confirmed.get(member.userId);
    if (overlay?.action === "removed") continue;
    visible.push(overlay?.member ?? member);
  }
  for (const [userId, overlay] of confirmed) {
    if (overlay.action === "added" && !returnedUserIds.has(userId)) {
      visible.push(overlay.member);
    }
  }
  return visible;
}

function withConfirmedTeamMutations(
  rawTeams: readonly TeamResponse[],
  confirmed: ReadonlyMap<string, ConfirmedTeamMutation>,
): readonly TeamResponse[] {
  const visible: TeamResponse[] = [];
  const returnedIds = new Set<string>();
  for (const team of rawTeams) {
    if (returnedIds.has(team.id)) continue;
    returnedIds.add(team.id);
    const overlay = confirmed.get(team.id);
    if (overlay?.action === "deleted") continue;
    visible.push(
      overlay?.action === "renamed"
        ? {
            ...team,
            name: overlay.team.name,
            updatedAt: overlay.team.updatedAt,
          }
        : overlay?.action === "created"
          ? overlay.team
          : team,
    );
  }
  const missingConfirmed = [...confirmed.values()].filter(
    (
      overlay,
    ): overlay is Extract<
      ConfirmedTeamMutation,
      { action: "created" | "renamed" }
    > => overlay.action !== "deleted" && !returnedIds.has(overlay.team.id),
  );
  return [
    ...missingConfirmed
      .filter((overlay) => overlay.action === "created")
      .toReversed()
      .map((overlay) => overlay.team),
    ...visible,
    ...missingConfirmed
      .filter((overlay) => overlay.action === "renamed")
      .map((overlay) => overlay.team),
  ];
}

function readPage<T>(
  result: Readonly<{
    data?: { data: T };
    error?: unknown;
    response?: Response;
  }>,
): { ok: true; data: T } | { ok: false; failure: ApiFailure } {
  return result.data !== undefined
    ? { ok: true, data: result.data.data }
    : {
        ok: false,
        failure: normalizeApiFailure(result.error, result.response),
      };
}

async function runRead<T>(
  operation: () => Promise<
    Readonly<{
      data?: { data: T };
      error?: unknown;
      response?: Response;
    }>
  >,
): Promise<{ ok: true; data: T } | { ok: false; failure: ApiFailure }> {
  try {
    return readPage(await operation());
  } catch {
    return {
      ok: false,
      failure: { kind: "network", code: "api_unavailable" },
    };
  }
}

function useAttachedAndVisible() {
  const attached = useRef(true);
  const visible = useRef(true);
  const queued = useRef<(() => void) | null>(null);
  useInsertionEffect(() => {
    attached.current = true;
    return () => {
      attached.current = false;
      queued.current = null;
    };
  }, []);
  useLayoutEffect(() => {
    visible.current = true;
    const effect = queued.current;
    queued.current = null;
    effect?.();
    return () => {
      visible.current = false;
    };
  }, []);
  return { attached, visible, queued };
}

function safeRefresh(
  router: ReturnType<typeof useRouter>,
  lifecycle: ReturnType<typeof useAttachedAndVisible>,
) {
  const run = () => router.refresh();
  if (lifecycle.visible.current) run();
  else lifecycle.queued.current = run;
}

function displayName(person: Pick<TeamMemberResponse, "name" | "email">) {
  return person.name.trim() || person.email;
}

function TeamMemberDirectory({
  onMemberCountChange,
  onMemberCountReconciled,
  organization,
  team,
}: Readonly<{
  onMemberCountChange: (teamId: string, delta: number) => void;
  onMemberCountReconciled: (teamId: string, count: number) => void;
  organization: OrganizationView;
  team: TeamResponse;
}>) {
  const t = useTranslations("collaboration.teams");
  const failures = useTranslations("collaboration.failures");
  const router = useRouter();
  const lifecycle = useAttachedAndVisible();
  const memberReadGeneration = useRef(0);
  const activeMemberRead = useRef<number | null>(null);
  const memberMutationGeneration = useRef(0);
  const queuedMemberRecovery = useRef(false);
  const generatedMemberCoverage = useRef<readonly TeamMemberResponse[] | null>(
    null,
  );
  const confirmedMemberMutationsRef = useRef(
    new Map<string, ConfirmedMemberMutation>(),
  );
  const mutationInFlight = useRef(new Set<string>());
  const [members, setMembers] = useState<readonly TeamMemberResponse[]>(
    mergeUniqueById([], team.members.items, (member) => member.id),
  );
  const [confirmedMemberMutations, setConfirmedMemberMutations] = useState<
    ReadonlyMap<string, ConfirmedMemberMutation>
  >(new Map());
  const [nextCursor, setNextCursor] = useState(team.members.nextCursor);
  const [loadingMore, setLoadingMore] = useState(false);
  const [partialFailure, setPartialFailure] = useState(false);
  const [savedMessage, setSavedMessage] = useState<string | null>(null);
  const [refreshFailure, setRefreshFailure] = useState(false);
  const [mutationFailure, setMutationFailure] = useState<ApiFailure | null>(
    null,
  );
  const [pendingRemoval, setPendingRemoval] = useState<string | null>(null);
  const [serverMembers, setServerMembers] = useState(team.members);

  useInsertionEffect(() => {
    memberReadGeneration.current += 1;
    activeMemberRead.current = null;
    generatedMemberCoverage.current = null;
    queuedMemberRecovery.current = confirmedMemberMutationsRef.current.size > 0;
  }, [team.members]);

  if (serverMembers !== team.members) {
    setServerMembers(team.members);
    setMembers(mergeUniqueById([], team.members.items, (member) => member.id));
    setNextCursor(team.members.nextCursor);
    setLoadingMore(false);
    setPartialFailure(false);
    setRefreshFailure(false);
  }

  const visibleMembers = withConfirmedMemberMutations(
    members,
    confirmedMemberMutations,
  );

  function acknowledgeMemberMutations(
    page: TeamMemberPageResponse,
    readGeneration: number,
    authoritativeAbsence: boolean,
  ) {
    const returnedByUserId = new Map(
      page.items.map((member) => [member.userId, member]),
    );
    const current = confirmedMemberMutationsRef.current;
    const remaining = new Map(current);
    for (const [userId, overlay] of current) {
      if (readGeneration <= overlay.confirmedAfterReadGeneration) continue;
      const observed =
        overlay.action === "added"
          ? returnedByUserId.get(userId)?.id === overlay.member.id
          : authoritativeAbsence && !returnedByUserId.has(userId);
      if (observed) remaining.delete(userId);
    }
    if (remaining.size === current.size) return;
    confirmedMemberMutationsRef.current = remaining;
    setConfirmedMemberMutations(remaining);
  }

  function confirmMemberMutation(
    action: "added" | "removed",
    member: TeamMemberResponse,
  ) {
    const confirmed = new Map(confirmedMemberMutationsRef.current);
    confirmed.set(member.userId, {
      action,
      member,
      confirmedAfterReadGeneration: memberReadGeneration.current,
    });
    confirmedMemberMutationsRef.current = confirmed;
    setConfirmedMemberMutations(confirmed);
  }

  async function readMembers(cursor?: string, replace = false) {
    if (activeMemberRead.current !== null) {
      if (replace) queuedMemberRecovery.current = true;
      return false;
    }
    const readGeneration = ++memberReadGeneration.current;
    const mutationGeneration = memberMutationGeneration.current;
    activeMemberRead.current = readGeneration;
    setLoadingMore(true);
    setPartialFailure(false);
    const result = await runRead<TeamMemberPageResponse>(() =>
      getTeamMembers({
        client: createBrowserApiClient(),
        path: { organizationId: organization.id, teamId: team.id },
        query: { ...(cursor ? { cursor } : {}), limit: 50 },
      }),
    );
    if (!lifecycle.attached.current) return false;
    if (activeMemberRead.current !== readGeneration) return false;
    activeMemberRead.current = null;
    setLoadingMore(false);
    const stale = mutationGeneration !== memberMutationGeneration.current;
    let refreshed = false;
    if (!stale) {
      if (!result.ok) {
        if (replace) setRefreshFailure(true);
        else setPartialFailure(true);
      } else {
        let acknowledgementPage = result.data;
        let authoritativeAbsence = false;
        if (replace) {
          const replacement = mergeUniqueById(
            [],
            result.data.items,
            (member) => member.id,
          );
          generatedMemberCoverage.current = replacement;
          setMembers(replacement);
          if (result.data.nextCursor === null) {
            authoritativeAbsence = true;
            acknowledgementPage = { ...result.data, items: [...replacement] };
            onMemberCountReconciled(team.id, replacement.length);
          }
        } else {
          setMembers((current) =>
            mergeUniqueById(current, result.data.items, (member) => member.id),
          );
          const generatedCoverage = generatedMemberCoverage.current;
          if (generatedCoverage !== null) {
            const accumulated = mergeUniqueById(
              generatedCoverage,
              result.data.items,
              (member) => member.id,
            );
            generatedMemberCoverage.current = accumulated;
            if (result.data.nextCursor === null) {
              authoritativeAbsence = true;
              acknowledgementPage = {
                ...result.data,
                items: [...accumulated],
              };
              onMemberCountReconciled(team.id, accumulated.length);
            }
          }
        }
        acknowledgeMemberMutations(
          acknowledgementPage,
          readGeneration,
          authoritativeAbsence,
        );
        setNextCursor(result.data.nextCursor);
        setPartialFailure(false);
        setRefreshFailure(false);
        refreshed = true;
      }
    }
    const recoverLatest = queuedMemberRecovery.current || stale;
    queuedMemberRecovery.current = false;
    if (recoverLatest) void readMembers(undefined, true);
    return refreshed;
  }

  useLayoutEffect(() => {
    if (!queuedMemberRecovery.current || activeMemberRead.current !== null) {
      return;
    }
    queuedMemberRecovery.current = false;
    void readMembers(undefined, true);
  });

  async function confirmMemberChange(
    action: "added" | "removed",
    member: TeamMemberResponse,
  ) {
    if (!lifecycle.attached.current) return;
    memberMutationGeneration.current += 1;
    generatedMemberCoverage.current = null;
    confirmMemberMutation(action, member);
    setSavedMessage(
      action === "added"
        ? t("success.memberAdded")
        : t("success.memberRemoved"),
    );
    onMemberCountChange(team.id, action === "added" ? 1 : -1);
    setMutationFailure(null);
    await readMembers(undefined, true);
    if (lifecycle.attached.current) safeRefresh(router, lifecycle);
  }

  async function remove(member: TeamMemberResponse) {
    const key = `remove:${member.userId}`;
    if (mutationInFlight.current.has(key)) return;
    mutationInFlight.current.add(key);
    setPendingRemoval(member.userId);
    setMutationFailure(null);
    const result = await removeBrowserTeamMember(
      createBrowserApiClient(),
      organization.id,
      team.id,
      member.userId,
    );
    if (!lifecycle.attached.current) return;
    mutationInFlight.current.delete(key);
    setPendingRemoval(null);
    if (
      !result.ok ||
      result.data.teamId !== team.id ||
      result.data.userId !== member.userId
    ) {
      setMutationFailure(
        result.ok
          ? { kind: "network", code: "api_unavailable" }
          : result.failure,
      );
      return;
    }
    await confirmMemberChange("removed", member);
  }

  const mutationMessage =
    mutationFailure?.kind === "problem"
      ? (new Map<string, string>([
          ["team_member_not_found", failures("codes.team_member_not_found")],
          ["team_not_found", failures("codes.team_not_found")],
          ["team_permission_denied", failures("codes.team_permission_denied")],
          ["antiforgery_failed", failures("codes.antiforgery_failed")],
        ]).get(mutationFailure.code) ?? failures("generic"))
      : failures("generic");

  return (
    <section
      aria-label={t("members.label", { team: team.name })}
      className="flex flex-col gap-3"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-medium">
          {t("members.label", { team: team.name })}
        </h3>
        {organization.canManageTeams ? (
          <TeamMemberCandidateDialog
            organizationId={organization.id}
            team={team}
            onConfirmed={(member) => confirmMemberChange("added", member)}
          />
        ) : null}
      </div>
      {savedMessage ? (
        <Alert>
          <AlertTitle>{savedMessage}</AlertTitle>
        </Alert>
      ) : null}
      {refreshFailure ? (
        <Alert variant="destructive">
          <AlertTitle>{t("success.refreshFailure")}</AlertTitle>
          <AlertDescription>
            <Button
              onClick={() => readMembers(undefined, true)}
              size="sm"
              type="button"
              variant="outline"
            >
              {t("actions.retry")}
            </Button>
          </AlertDescription>
        </Alert>
      ) : null}
      {mutationFailure ? (
        <Alert variant="destructive">
          <AlertTitle>{mutationMessage}</AlertTitle>
        </Alert>
      ) : null}
      {visibleMembers.length === 0 ? (
        <Empty className="border">
          <EmptyHeader>
            <EmptyMedia variant="icon">
              <IconUsers />
            </EmptyMedia>
            <EmptyTitle>{t("members.empty")}</EmptyTitle>
            <EmptyDescription>{t("list.emptyDescription")}</EmptyDescription>
          </EmptyHeader>
        </Empty>
      ) : (
        <Table className="min-w-[40rem]">
          <TableHeader>
            <TableRow>
              <TableHead>{t("members.columns.user")}</TableHead>
              <TableHead>{t("members.columns.email")}</TableHead>
              <TableHead>{t("members.columns.role")}</TableHead>
              {organization.canManageTeams ? (
                <TableHead className="text-right">
                  {t("members.columns.actions")}
                </TableHead>
              ) : null}
            </TableRow>
          </TableHeader>
          <TableBody>
            {visibleMembers.map((member) => {
              const name = displayName(member);
              const initials = name
                .split(/\s+/)
                .filter(Boolean)
                .slice(0, 2)
                .map((segment) => segment[0]?.toUpperCase() ?? "")
                .join("");
              return (
                <TableRow key={member.id}>
                  <TableCell className="min-w-48">
                    <div className="flex items-center gap-3">
                      <Avatar size="sm">
                        <AvatarFallback>{initials || "?"}</AvatarFallback>
                      </Avatar>
                      <span className="truncate font-medium">{name}</span>
                    </div>
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {member.email}
                  </TableCell>
                  <TableCell>
                    <Badge variant="outline">
                      {
                        {
                          member: t("roles.member"),
                          admin: t("roles.admin"),
                          owner: t("roles.owner"),
                        }[member.role]
                      }
                    </Badge>
                  </TableCell>
                  {organization.canManageTeams ? (
                    <TableCell className="text-right">
                      <Button
                        aria-label={t("actions.removeMemberNamed", {
                          name,
                        })}
                        disabled={pendingRemoval !== null}
                        onClick={() => remove(member)}
                        size="sm"
                        type="button"
                        variant="ghost"
                      >
                        {pendingRemoval === member.userId
                          ? t("actions.removingMember")
                          : t("actions.removeMember")}
                      </Button>
                    </TableCell>
                  ) : null}
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      )}
      {partialFailure ? (
        <Alert variant="destructive">
          <AlertTitle>{t("members.partialFailure")}</AlertTitle>
          <AlertDescription>
            <Button
              onClick={() => readMembers(nextCursor ?? undefined)}
              size="sm"
              type="button"
              variant="outline"
            >
              {t("actions.retry")}
            </Button>
          </AlertDescription>
        </Alert>
      ) : null}
      {nextCursor && !partialFailure ? (
        <Button
          disabled={loadingMore}
          onClick={() => readMembers(nextCursor)}
          type="button"
          variant="outline"
        >
          {loadingMore ? t("members.loadingMore") : t("members.loadMore")}
        </Button>
      ) : null}
    </section>
  );
}

function TeamMemberCandidateDialog({
  organizationId,
  team,
  onConfirmed,
}: Readonly<{
  organizationId: string;
  team: TeamResponse;
  onConfirmed: (member: TeamMemberResponse) => void | Promise<void>;
}>) {
  const t = useTranslations("collaboration.teams");
  const failures = useTranslations("collaboration.failures");
  const lifecycle = useAttachedAndVisible();
  const requestEpoch = useRef(0);
  const mutationInFlight = useRef(new Set<string>());
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [searchedQuery, setSearchedQuery] = useState("");
  const [candidates, setCandidates] = useState<
    readonly TeamCandidateResponse[]
  >([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [pendingCandidate, setPendingCandidate] = useState<string | null>(null);

  function closeAndReset() {
    requestEpoch.current += 1;
    setOpen(false);
    setQuery("");
    setSearchedQuery("");
    setCandidates([]);
    setNextCursor(null);
    setLoading(false);
    setFailure(null);
    setPendingCandidate(null);
  }

  async function search(event?: FormEvent<HTMLFormElement>, cursor?: string) {
    event?.preventDefault();
    const normalizedQuery = (cursor ? searchedQuery : query)
      .trim()
      .slice(0, 100);
    const epoch = ++requestEpoch.current;
    setLoading(true);
    setFailure(null);
    const result = await runRead<{
      items: TeamCandidateResponse[];
      nextCursor: string | null;
    }>(() =>
      getTeamMemberCandidates({
        client: createBrowserApiClient(),
        path: { organizationId, teamId: team.id },
        query: {
          ...(normalizedQuery ? { q: normalizedQuery } : {}),
          ...(cursor ? { cursor } : {}),
          limit: 20,
        },
      }),
    );
    if (!lifecycle.attached.current || requestEpoch.current !== epoch) return;
    setLoading(false);
    if (!result.ok) {
      setFailure(result.failure);
      return;
    }
    setSearchedQuery(normalizedQuery);
    setCandidates((current) =>
      mergeUniqueById(
        cursor ? current : [],
        result.data.items,
        (candidate) => candidate.memberId,
      ),
    );
    setNextCursor(result.data.nextCursor);
  }

  async function add(candidate: TeamCandidateResponse) {
    if (mutationInFlight.current.has(candidate.userId)) return;
    mutationInFlight.current.add(candidate.userId);
    setPendingCandidate(candidate.userId);
    setFailure(null);
    const result = await addBrowserTeamMember(
      createBrowserApiClient(),
      organizationId,
      team.id,
      {
        userId: candidate.userId,
      },
    );
    if (!lifecycle.attached.current) return;
    mutationInFlight.current.delete(candidate.userId);
    setPendingCandidate(null);
    if (!result.ok) {
      setFailure(result.failure);
      return;
    }
    closeAndReset();
    await onConfirmed(result.data);
  }

  const failureMessage =
    failure?.kind === "problem"
      ? (new Map<string, string>([
          [
            "team_member_already_exists",
            failures("codes.team_member_already_exists"),
          ],
          ["team_not_found", failures("codes.team_not_found")],
          ["team_permission_denied", failures("codes.team_permission_denied")],
          ["validation_failed", failures("codes.validation_failed")],
        ]).get(failure.code) ?? failures("generic"))
      : failures("generic");

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (mutationInFlight.current.size === 0) {
          if (next) setOpen(true);
          else closeAndReset();
        }
      }}
    >
      <DialogTrigger asChild>
        <Button
          aria-label={t("actions.addMemberNamed", { team: team.name })}
          size="sm"
          type="button"
        >
          {t("actions.addMember")}
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-lg" showCloseButton={false}>
        <DialogHeader>
          <DialogTitle>
            {t("actions.addMemberNamed", { team: team.name })}
          </DialogTitle>
          <DialogDescription>{t("candidates.description")}</DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-2 sm:flex-row sm:items-end"
          onSubmit={(event) => search(event)}
        >
          <Field className="min-w-0 flex-1">
            <FieldLabel htmlFor={`candidate-search-${team.id}`}>
              {t("candidates.label")}
            </FieldLabel>
            <Input
              id={`candidate-search-${team.id}`}
              maxLength={100}
              onChange={(event) => {
                requestEpoch.current += 1;
                setQuery(event.currentTarget.value);
                setSearchedQuery("");
                setCandidates([]);
                setNextCursor(null);
                setLoading(false);
                setFailure(null);
              }}
              placeholder={t("candidates.placeholder")}
              value={query}
            />
          </Field>
          <Button disabled={loading || pendingCandidate !== null} type="submit">
            {loading ? t("candidates.searching") : t("candidates.search")}
          </Button>
        </form>
        {failure ? (
          <Alert variant="destructive">
            <AlertTitle>{failureMessage}</AlertTitle>
          </Alert>
        ) : null}
        {candidates.length === 0 && searchedQuery ? (
          <p className="text-sm text-muted-foreground">
            {t("candidates.empty")}
          </p>
        ) : (
          <ul className="max-h-64 divide-y overflow-auto" role="list">
            {candidates.map((candidate) => (
              <li
                className="flex min-w-0 items-center justify-between gap-3 py-2"
                key={candidate.memberId}
              >
                <span className="min-w-0">
                  <span className="block truncate font-medium">
                    {candidate.name || candidate.email}
                  </span>
                  <span className="block truncate text-muted-foreground">
                    {candidate.email}
                  </span>
                </span>
                <Button
                  aria-label={t("actions.addMemberPerson", {
                    name: candidate.name || candidate.email,
                  })}
                  disabled={pendingCandidate !== null}
                  onClick={() => add(candidate)}
                  size="sm"
                  type="button"
                >
                  {pendingCandidate === candidate.userId
                    ? t("actions.addingMember")
                    : t("actions.addMember")}
                </Button>
              </li>
            ))}
          </ul>
        )}
        {nextCursor ? (
          <Button
            disabled={loading}
            onClick={() => search(undefined, nextCursor)}
            type="button"
            variant="outline"
          >
            {loading ? t("candidates.loadingMore") : t("candidates.loadMore")}
          </Button>
        ) : null}
        <DialogFooter>
          <DialogClose asChild>
            <Button
              disabled={pendingCandidate !== null}
              type="button"
              variant="outline"
            >
              {t("form.cancel")}
            </Button>
          </DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

export function TeamDirectory({
  initialPage,
  organization,
  showListHeading = true,
}: Readonly<{
  initialPage: TeamDirectoryPage;
  organization: OrganizationView;
  showListHeading?: boolean;
}>) {
  const t = useTranslations("collaboration.teams");
  const router = useRouter();
  const lifecycle = useAttachedAndVisible();
  const teamReadGeneration = useRef(0);
  const activeTeamRead = useRef<number | null>(null);
  const teamMutationGeneration = useRef(0);
  const queuedTeamRecovery = useRef(false);
  const confirmedTeamMutationsRef = useRef(
    new Map<string, ConfirmedTeamMutation>(),
  );
  const confirmedMemberCountsRef = useRef(
    new Map<string, ConfirmedMemberCount>(),
  );
  const [teams, setTeams] = useState<readonly TeamResponse[]>(
    mergeUniqueById([], initialPage.items, (team) => team.id),
  );
  const [confirmedTeamMutations, setConfirmedTeamMutations] = useState<
    ReadonlyMap<string, ConfirmedTeamMutation>
  >(new Map());
  const [nextCursor, setNextCursor] = useState(initialPage.nextCursor);
  const [loadingMore, setLoadingMore] = useState(false);
  const [partialFailure, setPartialFailure] = useState(false);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [refreshFailure, setRefreshFailure] = useState(false);
  const [serverPage, setServerPage] = useState(initialPage);
  const [confirmedMemberCounts, setConfirmedMemberCounts] = useState<
    ReadonlyMap<string, ConfirmedMemberCount>
  >(new Map());

  useInsertionEffect(() => {
    teamReadGeneration.current += 1;
    activeTeamRead.current = null;
    queuedTeamRecovery.current =
      confirmedTeamMutationsRef.current.size > 0 ||
      confirmedMemberCountsRef.current.size > 0;
  }, [initialPage]);

  if (serverPage !== initialPage) {
    setServerPage(initialPage);
    setTeams(mergeUniqueById([], initialPage.items, (team) => team.id));
    setNextCursor(initialPage.nextCursor);
    setLoadingMore(false);
    setPartialFailure(false);
    setRefreshFailure(false);
  }

  const visibleTeams = withConfirmedTeamMutations(
    teams,
    confirmedTeamMutations,
  );
  const teamsWithConfirmedMemberCounts = visibleTeams.map((team) => {
    const confirmedCount = confirmedMemberCounts.get(team.id);
    return confirmedCount === undefined
      ? team
      : { ...team, memberCount: confirmedCount.count };
  });

  function teamMutationId(mutation: ConfirmedTeamMutation): string {
    return mutation.action === "deleted" ? mutation.teamId : mutation.team.id;
  }

  function confirmTeamMutation(
    mutation:
      | Readonly<{ action: "created" | "renamed"; team: TeamResponse }>
      | Readonly<{ action: "deleted"; teamId: string }>,
  ) {
    teamMutationGeneration.current += 1;
    const confirmed = new Map(confirmedTeamMutationsRef.current);
    const overlay = {
      ...mutation,
      confirmedAfterReadGeneration: teamReadGeneration.current,
    } as ConfirmedTeamMutation;
    confirmed.delete(teamMutationId(overlay));
    confirmed.set(teamMutationId(overlay), overlay);
    confirmedTeamMutationsRef.current = confirmed;
    setConfirmedTeamMutations(confirmed);
  }

  function acknowledgeTeamMutations(
    page: TeamPageResponse,
    readGeneration: number,
    authoritativeAbsence: boolean,
  ) {
    const returnedById = new Map(page.items.map((team) => [team.id, team]));
    const current = confirmedTeamMutationsRef.current;
    const remaining = new Map(current);
    for (const [teamId, overlay] of current) {
      if (readGeneration <= overlay.confirmedAfterReadGeneration) continue;
      const returned = returnedById.get(teamId);
      const observed =
        overlay.action === "created"
          ? returned !== undefined
          : overlay.action === "renamed"
            ? returned?.name === overlay.team.name
            : authoritativeAbsence && returned === undefined;
      if (observed) remaining.delete(teamId);
    }
    if (remaining.size === current.size) return;
    confirmedTeamMutationsRef.current = remaining;
    setConfirmedTeamMutations(remaining);
  }

  function acknowledgeMemberCountProjections(
    page: TeamPageResponse,
    readGeneration: number,
  ) {
    const returnedIds = new Set(page.items.map((team) => team.id));
    const current = confirmedMemberCountsRef.current;
    const remaining = new Map(current);
    for (const [teamId, overlay] of current) {
      if (
        readGeneration > overlay.confirmedAfterReadGeneration &&
        returnedIds.has(teamId)
      ) {
        remaining.delete(teamId);
      }
    }
    if (remaining.size === current.size) return;
    confirmedMemberCountsRef.current = remaining;
    setConfirmedMemberCounts(remaining);
  }

  async function readTeams(
    cursor?: string,
    recovery = false,
    reconcileFirstPage = false,
  ) {
    if (activeTeamRead.current !== null) {
      if (recovery) queuedTeamRecovery.current = true;
      return false;
    }
    const readGeneration = ++teamReadGeneration.current;
    const mutationGeneration = teamMutationGeneration.current;
    activeTeamRead.current = readGeneration;
    setLoadingMore(true);
    setPartialFailure(false);
    const result = await runRead<TeamPageResponse>(() =>
      getTeams({
        client: createBrowserApiClient(),
        path: { organizationId: organization.id },
        query: { ...(cursor ? { cursor } : {}), limit: 20 },
      }),
    );
    if (!lifecycle.attached.current) return false;
    if (activeTeamRead.current !== readGeneration) return false;
    activeTeamRead.current = null;
    setLoadingMore(false);
    const stale = mutationGeneration !== teamMutationGeneration.current;
    let refreshed = false;
    if (!stale) {
      if (!result.ok) {
        if (recovery) setRefreshFailure(true);
        else setPartialFailure(true);
      } else {
        if (cursor || reconcileFirstPage) {
          setTeams((current) =>
            mergeUniqueById(
              cursor ? current : [],
              result.data.items,
              (team) => team.id,
            ),
          );
          setNextCursor(result.data.nextCursor);
          acknowledgeTeamMutations(
            result.data,
            readGeneration,
            !cursor && reconcileFirstPage && result.data.nextCursor === null,
          );
          acknowledgeMemberCountProjections(result.data, readGeneration);
        }
        setRefreshFailure(false);
        setPartialFailure(false);
        refreshed = true;
      }
    }
    const recoverLatest = queuedTeamRecovery.current || stale;
    queuedTeamRecovery.current = false;
    if (recoverLatest) void readTeams(undefined, true, true);
    return refreshed;
  }

  useLayoutEffect(() => {
    if (!queuedTeamRecovery.current || activeTeamRead.current !== null) return;
    queuedTeamRecovery.current = false;
    void readTeams(undefined, true, true);
  });

  async function recoverAfterMutation() {
    await readTeams(undefined, true, true);
    if (lifecycle.attached.current) safeRefresh(router, lifecycle);
  }

  async function created(team: TeamResponse) {
    confirmTeamMutation({ action: "created", team });
    setFeedback(t("success.created"));
    await recoverAfterMutation();
  }
  async function renamed(team: TeamResponse) {
    confirmTeamMutation({ action: "renamed", team });
    setFeedback(t("success.renamed"));
    await recoverAfterMutation();
  }
  async function deleted(teamId: string) {
    confirmTeamMutation({ action: "deleted", teamId });
    setFeedback(t("success.deleted"));
    await recoverAfterMutation();
  }

  function changeMemberCount(teamId: string, delta: number) {
    const current = confirmedMemberCountsRef.current;
    const team = teamsWithConfirmedMemberCounts.find(
      (candidate) => candidate.id === teamId,
    );
    if (!team) return;
    teamMutationGeneration.current += 1;
    const confirmed = new Map(current);
    confirmed.set(teamId, {
      count: Math.max(
        0,
        (current.get(teamId)?.count ?? team.memberCount) + delta,
      ),
      confirmedAfterReadGeneration: teamReadGeneration.current,
    });
    confirmedMemberCountsRef.current = confirmed;
    setConfirmedMemberCounts(confirmed);
  }

  function reconcileMemberCount(teamId: string, count: number) {
    teamMutationGeneration.current += 1;
    setTeams((current) =>
      current.map((team) =>
        team.id === teamId ? { ...team, memberCount: count } : team,
      ),
    );
    const teamMutation = confirmedTeamMutationsRef.current.get(teamId);
    if (teamMutation && teamMutation.action !== "deleted") {
      const confirmed = new Map(confirmedTeamMutationsRef.current);
      confirmed.set(teamId, {
        ...teamMutation,
        team: { ...teamMutation.team, memberCount: count },
      });
      confirmedTeamMutationsRef.current = confirmed;
      setConfirmedTeamMutations(confirmed);
    }
    const confirmed = new Map(confirmedMemberCountsRef.current);
    confirmed.set(teamId, {
      count,
      confirmedAfterReadGeneration: teamReadGeneration.current,
    });
    confirmedMemberCountsRef.current = confirmed;
    setConfirmedMemberCounts(confirmed);
  }

  return (
    <section
      aria-label={t("list.label")}
      className="flex flex-col gap-5"
      role="region"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        {showListHeading ? (
          <h2 className="text-lg font-medium">{t("list.label")}</h2>
        ) : null}
        {organization.canManageTeams ? (
          <TeamCreateDialog
            onConfirmed={created}
            organizationId={organization.id}
          />
        ) : null}
      </div>
      {!organization.canManageTeams ? (
        <Alert>
          <AlertTitle>{t("readOnly.title")}</AlertTitle>
          <AlertDescription>{t("readOnly.description")}</AlertDescription>
        </Alert>
      ) : null}
      {feedback ? (
        <Alert>
          <AlertTitle>{feedback}</AlertTitle>
        </Alert>
      ) : null}
      {refreshFailure ? (
        <Alert variant="destructive">
          <AlertTitle>{t("success.refreshFailure")}</AlertTitle>
          <AlertDescription>
            <Button
              onClick={() => readTeams(undefined, true, true)}
              size="sm"
              type="button"
              variant="outline"
            >
              {t("actions.retry")}
            </Button>
          </AlertDescription>
        </Alert>
      ) : null}
      {teamsWithConfirmedMemberCounts.length === 0 ? (
        <Empty className="border">
          <EmptyHeader>
            <EmptyMedia variant="icon">
              <IconUsers />
            </EmptyMedia>
            <EmptyTitle>{t("list.emptyTitle")}</EmptyTitle>
            <EmptyDescription>{t("list.emptyDescription")}</EmptyDescription>
          </EmptyHeader>
        </Empty>
      ) : (
        <div className="flex flex-col gap-5">
          {teamsWithConfirmedMemberCounts.map((team) => (
            <Card key={team.id}>
              <CardHeader>
                <CardTitle className="flex min-w-0 flex-wrap items-center gap-2">
                  <span className="truncate">{team.name}</span>
                  <Badge variant="outline">
                    {t("members.count", { count: team.memberCount })}
                  </Badge>
                </CardTitle>
                <CardDescription>
                  {t("members.label", { team: team.name })}
                </CardDescription>
                {organization.canManageTeams ? (
                  <CardAction className="flex flex-wrap justify-end gap-2">
                    <TeamRenameDialog
                      key={`rename-${team.id}`}
                      onConfirmed={renamed}
                      organizationId={organization.id}
                      team={team}
                    />
                    <TeamDeleteDialog
                      key={`delete-${team.id}`}
                      onConfirmed={deleted}
                      organizationId={organization.id}
                      team={team}
                    />
                  </CardAction>
                ) : null}
              </CardHeader>
              <CardContent className="flex flex-col gap-4">
                <TeamMemberDirectory
                  key={`${organization.id}:${team.id}`}
                  onMemberCountChange={changeMemberCount}
                  onMemberCountReconciled={reconcileMemberCount}
                  organization={organization}
                  team={team}
                />
              </CardContent>
            </Card>
          ))}
        </div>
      )}
      {partialFailure ? (
        <Alert variant="destructive">
          <AlertTitle>{t("list.partialFailure")}</AlertTitle>
          <AlertDescription>
            <Button
              onClick={() => readTeams(nextCursor ?? undefined)}
              size="sm"
              type="button"
              variant="outline"
            >
              {t("actions.retry")}
            </Button>
          </AlertDescription>
        </Alert>
      ) : null}
      {nextCursor && !partialFailure ? (
        <Button
          disabled={loadingMore}
          onClick={() => readTeams(nextCursor)}
          type="button"
          variant="outline"
        >
          {loadingMore ? t("list.loadingMore") : t("list.loadMore")}
        </Button>
      ) : null}
    </section>
  );
}
