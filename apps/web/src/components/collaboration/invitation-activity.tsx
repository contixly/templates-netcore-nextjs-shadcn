"use client";

import { IconMail } from "@tabler/icons-react";
import { useLocale, useTranslations } from "next-intl";
import { useInsertionEffect, useLayoutEffect, useRef, useState } from "react";

import { InvitationCopyButton } from "@/src/components/collaboration/invitation-copy-button";
import { InvitationCreateDialog } from "@/src/components/collaboration/invitation-create-dialog";
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
import { Field, FieldLabel } from "@/src/components/ui/field";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/src/components/ui/select";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getOrganizationInvitations } from "@/src/lib/api/generated/sdk.gen";
import type {
  GetOrganizationInvitationsData,
  InvitationResponse,
  OrganizationDetailResponse,
  OrganizationInvitationPageResponse,
} from "@/src/lib/api/generated/types.gen";

type InvitationFilter =
  | NonNullable<NonNullable<GetOrganizationInvitationsData["query"]>["status"]>
  | "all";

const filters: readonly InvitationFilter[] = [
  "all",
  "pending",
  "accepted",
  "rejected",
  "canceled",
  "expired",
];

function includesConfirmedInvitation(filter: InvitationFilter): boolean {
  return filter === "all" || filter === "pending";
}

function latestUnique(
  current: readonly InvitationResponse[],
  incoming: readonly InvitationResponse[],
): InvitationResponse[] {
  const merged = new Map(current.map((item) => [item.id, item]));
  for (const item of incoming) merged.set(item.id, item);
  return [...merged.values()];
}

function formattedDate(value: string, locale: string): string {
  const date = new Date(value);
  return Number.isNaN(date.valueOf())
    ? value
    : new Intl.DateTimeFormat(locale, {
        dateStyle: "medium",
        timeStyle: "short",
        timeZone: "UTC",
      }).format(date);
}

function badgeVariant(state: InvitationResponse["displayState"]) {
  return state === "pending"
    ? ("default" as const)
    : state === "accepted"
      ? ("secondary" as const)
      : ("outline" as const);
}

export function InvitationActivity({
  initialPage,
  organization,
  teams,
}: Readonly<{
  initialPage: OrganizationInvitationPageResponse;
  organization: Readonly<{
    id: string;
    currentRole: OrganizationDetailResponse["currentRole"];
  }>;
  teams: readonly Readonly<{ id: string; name: string }>[];
}>) {
  const t = useTranslations("collaboration.invitations");
  const locale = useLocale();
  const [serverPage, setServerPage] = useState(initialPage);
  const [items, setItems] = useState<readonly InvitationResponse[]>(
    latestUnique([], initialPage.items),
  );
  const [nextCursor, setNextCursor] = useState(initialPage.nextCursor);
  const [filter, setFilter] = useState<InvitationFilter>("all");
  const [confirmedItems, setConfirmedItems] = useState<
    readonly InvitationResponse[]
  >([]);
  const [pending, setPending] = useState(false);
  const [partialFailure, setPartialFailure] = useState(false);
  const [failedCursor, setFailedCursor] = useState<string | undefined>();
  const requestEpoch = useRef(0);
  const activeRequest = useRef<
    Readonly<{ epoch: number; filter: InvitationFilter }> | undefined
  >(undefined);
  const confirmedGeneration = useRef(0);
  const confirmedInvitationIds = useRef(new Set<string>());
  const queuedReconciliation = useRef(false);
  const queuedServerPageReconciliation = useRef(false);
  const filterRef = useRef<InvitationFilter>("all");

  useInsertionEffect(() => {
    requestEpoch.current += 1;
    activeRequest.current = undefined;
    queuedReconciliation.current = false;
    for (const item of initialPage.items) {
      confirmedInvitationIds.current.delete(item.id);
    }
    queuedServerPageReconciliation.current =
      confirmedInvitationIds.current.size > 0;
    filterRef.current = "all";
    return () => {
      requestEpoch.current += 1;
      activeRequest.current = undefined;
      queuedReconciliation.current = false;
      queuedServerPageReconciliation.current = false;
    };
  }, [initialPage]);

  useLayoutEffect(() => {
    if (!queuedServerPageReconciliation.current) return;
    queuedServerPageReconciliation.current = false;
    void read("all", undefined, true);
  });

  function acknowledgeConfirmedInvitations(
    serverItems: readonly InvitationResponse[],
  ) {
    if (serverItems.length === 0) return;
    const returnedIds = new Set(serverItems.map((item) => item.id));
    for (const id of returnedIds) confirmedInvitationIds.current.delete(id);
    setConfirmedItems((current) => {
      const remaining = current.filter((item) => !returnedIds.has(item.id));
      return remaining.length === current.length ? current : remaining;
    });
  }

  if (serverPage !== initialPage) {
    const returnedIds = new Set(initialPage.items.map((item) => item.id));
    setServerPage(initialPage);
    setItems(latestUnique([], initialPage.items));
    setConfirmedItems((current) =>
      current.filter((item) => !returnedIds.has(item.id)),
    );
    setNextCursor(initialPage.nextCursor);
    setFilter("all");
    setPending(false);
    setPartialFailure(false);
    setFailedCursor(undefined);
  }

  const visibleItems = includesConfirmedInvitation(filter)
    ? latestUnique(items, confirmedItems)
    : items;

  async function read(
    nextFilter: InvitationFilter,
    cursor?: string,
    reconciliation = false,
  ) {
    const request = ++requestEpoch.current;
    const mutationGeneration = confirmedGeneration.current;
    activeRequest.current = { epoch: request, filter: nextFilter };
    if (includesConfirmedInvitation(nextFilter)) {
      queuedReconciliation.current = false;
    }
    if (!cursor && !reconciliation) {
      setItems([]);
      setNextCursor(null);
    }
    setPending(true);
    setPartialFailure(false);
    setFailedCursor(undefined);
    try {
      const result = await getOrganizationInvitations({
        client: createBrowserApiClient(),
        cache: "no-store",
        path: { organizationId: organization.id },
        query: {
          ...(nextFilter === "all" ? {} : { status: nextFilter }),
          ...(cursor ? { cursor } : {}),
          limit: 20,
        },
      });
      if (requestEpoch.current !== request) return;
      if (
        includesConfirmedInvitation(nextFilter) &&
        confirmedGeneration.current !== mutationGeneration
      ) {
        queuedReconciliation.current = true;
        return;
      }
      if (result.data === undefined) {
        normalizeApiFailure(result.error, result.response);
        setPartialFailure(true);
        setFailedCursor(cursor);
        return;
      }
      const page = result.data.data;
      acknowledgeConfirmedInvitations(page.items);
      setItems((current) =>
        cursor
          ? latestUnique(current, page.items)
          : latestUnique([], page.items),
      );
      setNextCursor(page.nextCursor);
    } catch (error) {
      if (requestEpoch.current !== request) return;
      if (
        includesConfirmedInvitation(nextFilter) &&
        confirmedGeneration.current !== mutationGeneration
      ) {
        queuedReconciliation.current = true;
        return;
      }
      normalizeApiFailure(error);
      setPartialFailure(true);
      setFailedCursor(cursor);
    } finally {
      if (requestEpoch.current === request) {
        activeRequest.current = undefined;
        setPending(false);
        if (
          queuedReconciliation.current &&
          includesConfirmedInvitation(filterRef.current)
        ) {
          queuedReconciliation.current = false;
          void read(filterRef.current, undefined, true);
        }
      }
    }
  }

  function invitationCreated(invitation: InvitationResponse) {
    if (
      invitation.status !== "pending" ||
      invitation.displayState !== "pending"
    )
      return;
    confirmedInvitationIds.current.add(invitation.id);
    confirmedGeneration.current += 1;
    setConfirmedItems((current) => latestUnique(current, [invitation]));
    const currentFilter = filterRef.current;
    if (!includesConfirmedInvitation(currentFilter)) return;
    const active = activeRequest.current;
    if (
      active &&
      active.epoch === requestEpoch.current &&
      includesConfirmedInvitation(active.filter)
    ) {
      queuedReconciliation.current = true;
    } else {
      void read(currentFilter, undefined, true);
    }
  }

  return (
    <section aria-label={t("activity.label")} className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <Field className="w-full max-w-48">
          <FieldLabel htmlFor="invitation-status-filter">
            {t("filters.status")}
          </FieldLabel>
          <Select
            onValueChange={(value) => {
              if (!filters.includes(value as InvitationFilter)) return;
              const next = value as InvitationFilter;
              filterRef.current = next;
              if (!includesConfirmedInvitation(next)) {
                queuedReconciliation.current = false;
              }
              setFilter(next);
              void read(next);
            }}
            value={filter}
          >
            <SelectTrigger id="invitation-status-filter">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                {filters.map((value) => (
                  <SelectItem key={value} value={value}>
                    {value === "all" ? t("filters.all") : t(`status.${value}`)}
                  </SelectItem>
                ))}
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>
        <InvitationCreateDialog
          currentRole={organization.currentRole}
          onConfirmed={invitationCreated}
          organizationId={organization.id}
          teams={teams}
        />
      </div>

      {partialFailure ? (
        <Alert variant="destructive">
          <AlertTitle>{t("activity.partialFailure")}</AlertTitle>
          <AlertDescription>
            <Button
              disabled={pending}
              onClick={() => void read(filter, failedCursor)}
              type="button"
              variant="outline"
            >
              {t("activity.retry")}
            </Button>
          </AlertDescription>
        </Alert>
      ) : null}

      {visibleItems.length === 0 ? (
        <Empty className="border">
          <EmptyHeader>
            <EmptyMedia variant="icon">
              <IconMail />
            </EmptyMedia>
            <EmptyTitle>{t("activity.empty")}</EmptyTitle>
            <EmptyDescription>{t("settings.description")}</EmptyDescription>
          </EmptyHeader>
        </Empty>
      ) : (
        <div className="flex flex-col gap-3">
          {visibleItems.map((invitation) => (
            <Card key={invitation.id} size="sm">
              <CardHeader>
                <CardTitle>{invitation.email}</CardTitle>
                <CardDescription>
                  {t("item.inviter", { name: invitation.inviterName })}
                </CardDescription>
                <CardAction>
                  <Badge variant={badgeVariant(invitation.displayState)}>
                    {t(`status.${invitation.displayState}`)}
                  </Badge>
                </CardAction>
              </CardHeader>
              <CardContent className="flex flex-col gap-3">
                <div className="flex flex-wrap gap-2">
                  <Badge variant="outline">
                    {t(`roles.${invitation.role}`)}
                  </Badge>
                  {invitation.teamName ? (
                    <Badge variant="secondary">
                      {t("item.team", { team: invitation.teamName })}
                    </Badge>
                  ) : null}
                </div>
                <p className="text-muted-foreground">
                  {t("item.expires", {
                    date: formattedDate(invitation.expiresAt, locale),
                  })}
                </p>
                <InvitationCopyButton
                  invitationId={invitation.id}
                  invitationPath={invitation.invitationPath}
                />
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {nextCursor && !partialFailure ? (
        <Button
          disabled={pending}
          onClick={() => void read(filter, nextCursor)}
          type="button"
          variant="outline"
        >
          {pending ? t("activity.loadingMore") : t("activity.loadMore")}
        </Button>
      ) : null}
    </section>
  );
}
