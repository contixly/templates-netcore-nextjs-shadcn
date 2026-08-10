"use client";

import { IconMail } from "@tabler/icons-react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { useInsertionEffect, useRef, useState } from "react";

import { Alert, AlertTitle } from "@/src/components/ui/alert";
import { Badge } from "@/src/components/ui/badge";
import { Button } from "@/src/components/ui/button";
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/src/components/ui/empty";
import { collaborationRoutes } from "@/src/features/collaboration/collaboration-routes";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getAccountInvitations } from "@/src/lib/api/generated/sdk.gen";
import type {
  AccountInvitationPageResponse,
  InvitationResponse,
} from "@/src/lib/api/generated/types.gen";

function appendUnique(
  current: readonly InvitationResponse[],
  incoming: readonly InvitationResponse[],
) {
  const byId = new Map(current.map((item) => [item.id, item]));
  for (const item of incoming) byId.set(item.id, item);
  return [...byId.values()];
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

export function AccountInvitationList({
  initialPage,
  showEmptyState = true,
}: Readonly<{
  initialPage: AccountInvitationPageResponse;
  showEmptyState?: boolean;
}>) {
  const t = useTranslations("collaboration.invitations");
  const locale = useLocale();
  const [serverPage, setServerPage] = useState(initialPage);
  const [items, setItems] = useState<readonly InvitationResponse[]>(
    appendUnique([], initialPage.items),
  );
  const [nextCursor, setNextCursor] = useState(initialPage.nextCursor);
  const [pending, setPending] = useState(false);
  const [partialFailure, setPartialFailure] = useState(false);
  const requestGeneration = useRef(0);

  useInsertionEffect(() => {
    requestGeneration.current += 1;
    return () => {
      requestGeneration.current += 1;
    };
  }, [initialPage]);

  if (serverPage !== initialPage) {
    setServerPage(initialPage);
    setItems(appendUnique([], initialPage.items));
    setNextCursor(initialPage.nextCursor);
    setPending(false);
    setPartialFailure(false);
  }

  async function loadMore() {
    if (!nextCursor || pending) return;
    const generation = ++requestGeneration.current;
    setPending(true);
    setPartialFailure(false);
    try {
      const result = await getAccountInvitations({
        client: createBrowserApiClient(),
        cache: "no-store",
        query: { cursor: nextCursor, limit: 20 },
      });
      if (requestGeneration.current !== generation) return;
      if (result.data === undefined) {
        normalizeApiFailure(result.error, result.response);
        setPartialFailure(true);
        return;
      }
      setItems((current) => appendUnique(current, result.data!.data.items));
      setNextCursor(result.data.data.nextCursor);
    } catch (error) {
      if (requestGeneration.current !== generation) return;
      normalizeApiFailure(error);
      setPartialFailure(true);
    } finally {
      if (requestGeneration.current === generation) setPending(false);
    }
  }

  if (items.length === 0) {
    if (!showEmptyState) return null;
    return (
      <Empty className="border">
        <EmptyHeader>
          <EmptyMedia variant="icon">
            <IconMail />
          </EmptyMedia>
          <EmptyTitle>{t("account.emptyTitle")}</EmptyTitle>
          <EmptyDescription>{t("account.emptyDescription")}</EmptyDescription>
        </EmptyHeader>
      </Empty>
    );
  }

  return (
    <section
      aria-label={t("account.listLabel")}
      className="flex flex-col gap-3"
    >
      {partialFailure ? (
        <Alert variant="destructive">
          <AlertTitle>{t("account.partialFailure")}</AlertTitle>
        </Alert>
      ) : null}
      <div className="flex flex-col gap-4">
        {items.map((invitation) => (
          <article className="border p-4" key={invitation.id}>
            <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
              <div className="flex min-w-0 flex-col gap-2">
                <div className="flex flex-wrap items-center gap-2">
                  <h3 className="text-sm font-medium">
                    {invitation.organizationName}
                  </h3>
                  <Badge variant="outline">{t("status.pending")}</Badge>
                </div>
                <p className="text-sm text-muted-foreground">
                  {t("item.recipient", { email: invitation.email })}
                </p>
                <p className="text-sm text-muted-foreground">
                  {t("item.inviter", { name: invitation.inviterName })}
                </p>
                <div className="flex flex-wrap gap-2">
                  <Badge variant="secondary">
                    {t(`roles.${invitation.role}`)}
                  </Badge>
                  {invitation.teamName ? (
                    <Badge variant="secondary">
                      {t("item.team", { team: invitation.teamName })}
                    </Badge>
                  ) : null}
                </div>
                <p className="text-sm text-muted-foreground">
                  {t("item.expires", {
                    date: formattedDate(invitation.expiresAt, locale),
                  })}
                </p>
              </div>
              <Button asChild className="min-w-fit">
                <Link
                  href={collaborationRoutes.invitationDecision(invitation.id)}
                >
                  {t("item.review")}
                </Link>
              </Button>
            </div>
          </article>
        ))}
      </div>
      {nextCursor ? (
        <Button
          disabled={pending}
          onClick={loadMore}
          type="button"
          variant="outline"
        >
          {pending ? t("activity.loadingMore") : t("activity.loadMore")}
        </Button>
      ) : null}
    </section>
  );
}
