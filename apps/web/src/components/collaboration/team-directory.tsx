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
} from "@/src/components/collaboration/team-controls";
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
  organization,
  team,
}: Readonly<{
  onMemberCountChange: (teamId: string, delta: number) => void;
  organization: OrganizationView;
  team: TeamResponse;
}>) {
  const t = useTranslations("collaboration.teams");
  const failures = useTranslations("collaboration.failures");
  const router = useRouter();
  const lifecycle = useAttachedAndVisible();
  const memberRead = useRef(false);
  const mutationInFlight = useRef(new Set<string>());
  const [members, setMembers] = useState<readonly TeamMemberResponse[]>(
    mergeUniqueById([], team.members.items, (member) => member.id),
  );
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

  if (serverMembers !== team.members) {
    setServerMembers(team.members);
    setMembers(mergeUniqueById([], team.members.items, (member) => member.id));
    setNextCursor(team.members.nextCursor);
    setPartialFailure(false);
    setRefreshFailure(false);
  }

  async function readMembers(cursor?: string, replace = false) {
    if (memberRead.current) return false;
    memberRead.current = true;
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
    memberRead.current = false;
    setLoadingMore(false);
    if (!result.ok) {
      if (replace) setRefreshFailure(true);
      else setPartialFailure(true);
      return false;
    }
    if (replace) {
      setMembers(mergeUniqueById([], result.data.items, (member) => member.id));
    } else {
      setMembers((current) =>
        mergeUniqueById(current, result.data.items, (member) => member.id),
      );
    }
    setNextCursor(result.data.nextCursor);
    setPartialFailure(false);
    setRefreshFailure(false);
    return true;
  }

  async function confirmMemberChange(
    action: "added" | "removed",
    member: TeamMemberResponse,
  ) {
    if (!lifecycle.attached.current) return;
    setMembers((current) =>
      action === "added"
        ? current.some((item) => item.userId === member.userId)
          ? current
          : [...current, member]
        : current.filter((item) => item.userId !== member.userId),
    );
    setSavedMessage(
      action === "added"
        ? t("success.memberAdded")
        : t("success.memberRemoved"),
    );
    onMemberCountChange(team.id, action === "added" ? 1 : -1);
    setMutationFailure(null);
    const refreshed = await readMembers(undefined, true);
    if (refreshed && lifecycle.attached.current) {
      // The mutation response is the newest authority until the next server render.
      setMembers((current) =>
        action === "added"
          ? current.some((item) => item.userId === member.userId)
            ? current
            : [...current, member]
          : current.filter((item) => item.userId !== member.userId),
      );
    }
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
      {members.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("members.empty")}</p>
      ) : (
        <ul className="divide-y border" role="list">
          {members.map((member) => (
            <li
              className="flex items-center justify-between gap-3 p-3"
              key={member.id}
            >
              <span className="min-w-0">
                <span className="block truncate font-medium">
                  {displayName(member)}
                </span>
                <span className="block truncate text-muted-foreground">
                  {member.email}
                </span>
              </span>
              <span className="flex items-center gap-2">
                <Badge variant="outline">
                  {
                    {
                      member: t("roles.member"),
                      admin: t("roles.admin"),
                      owner: t("roles.owner"),
                    }[member.role]
                  }
                </Badge>
                {organization.canManageTeams ? (
                  <Button
                    aria-label={t("actions.removeMemberNamed", {
                      name: displayName(member),
                    })}
                    disabled={pendingRemoval !== null}
                    onClick={() => remove(member)}
                    size="sm"
                    type="button"
                    variant="outline"
                  >
                    {pendingRemoval === member.userId
                      ? t("actions.removingMember")
                      : t("actions.removeMember")}
                  </Button>
                ) : null}
              </span>
            </li>
          ))}
        </ul>
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
    setCandidates((current) =>
      current.filter((item) => item.userId !== candidate.userId),
    );
    setOpen(false);
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
          setOpen(next);
          if (!next) {
            requestEpoch.current += 1;
            setQuery("");
            setSearchedQuery("");
            setCandidates([]);
            setNextCursor(null);
            setFailure(null);
          }
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
          className="flex items-end gap-2"
          onSubmit={(event) => search(event)}
        >
          <Field>
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
                className="flex items-center justify-between gap-3 py-2"
                key={candidate.memberId}
              >
                <span>
                  <span className="block font-medium">
                    {candidate.name || candidate.email}
                  </span>
                  <span className="block text-muted-foreground">
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
}: Readonly<{
  initialPage: TeamDirectoryPage;
  organization: OrganizationView;
}>) {
  const t = useTranslations("collaboration.teams");
  const router = useRouter();
  const lifecycle = useAttachedAndVisible();
  const readInFlight = useRef(false);
  const [teams, setTeams] = useState<readonly TeamResponse[]>(
    mergeUniqueById([], initialPage.items, (team) => team.id),
  );
  const [nextCursor, setNextCursor] = useState(initialPage.nextCursor);
  const [loadingMore, setLoadingMore] = useState(false);
  const [partialFailure, setPartialFailure] = useState(false);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [refreshFailure, setRefreshFailure] = useState(false);
  const [serverPage, setServerPage] = useState(initialPage);

  if (serverPage !== initialPage) {
    setServerPage(initialPage);
    setTeams(mergeUniqueById([], initialPage.items, (team) => team.id));
    setNextCursor(initialPage.nextCursor);
    setPartialFailure(false);
    setRefreshFailure(false);
  }

  async function readTeams(
    cursor?: string,
    recovery = false,
    reconcileFirstPage = false,
  ) {
    if (readInFlight.current) return false;
    readInFlight.current = true;
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
    readInFlight.current = false;
    setLoadingMore(false);
    if (!result.ok) {
      if (recovery) setRefreshFailure(true);
      else setPartialFailure(true);
      return false;
    }
    if (cursor || reconcileFirstPage) {
      setTeams((current) =>
        mergeUniqueById(
          cursor ? current : [],
          result.data.items,
          (team) => team.id,
        ),
      );
      setNextCursor(result.data.nextCursor);
    }
    setRefreshFailure(false);
    setPartialFailure(false);
    return true;
  }

  async function recoverAfterMutation() {
    await readTeams(undefined, true);
    if (lifecycle.attached.current) safeRefresh(router, lifecycle);
  }

  async function created(team: TeamResponse) {
    setTeams((current) => [
      team,
      ...current.filter((item) => item.id !== team.id),
    ]);
    setFeedback(t("success.created"));
    await recoverAfterMutation();
  }
  async function renamed(team: TeamResponse) {
    setTeams((current) =>
      current.map((item) => (item.id === team.id ? team : item)),
    );
    setFeedback(t("success.renamed"));
    await recoverAfterMutation();
  }
  async function deleted(teamId: string) {
    setTeams((current) => current.filter((item) => item.id !== teamId));
    setFeedback(t("success.deleted"));
    await recoverAfterMutation();
  }

  function changeMemberCount(teamId: string, delta: number) {
    setTeams((current) =>
      current.map((team) =>
        team.id === teamId
          ? { ...team, memberCount: Math.max(0, team.memberCount + delta) }
          : team,
      ),
    );
  }

  return (
    <section
      aria-label={t("list.label")}
      className="flex flex-col gap-5"
      role="region"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-medium">{t("list.label")}</h2>
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
      {teams.length === 0 ? (
        <Empty>
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
          {teams.map((team) => (
            <Card key={team.id}>
              <CardHeader>
                <CardTitle>{team.name}</CardTitle>
                <CardDescription>
                  {t("members.count", { count: team.memberCount })}
                </CardDescription>
                {organization.canManageTeams ? (
                  <CardAction className="flex gap-2">
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
              <CardContent>
                <TeamMemberDirectory
                  key={`${organization.id}:${team.id}`}
                  onMemberCountChange={changeMemberCount}
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
