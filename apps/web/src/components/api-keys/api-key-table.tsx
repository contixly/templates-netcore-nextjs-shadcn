"use client";

import { useLocale, useTranslations } from "next-intl";
import type { RefObject } from "react";

import { ApiKeyEditDialog } from "@/src/components/api-keys/api-key-edit-dialog";
import { ApiKeyRevokeDialog } from "@/src/components/api-keys/api-key-revoke-dialog";
import { ApiKeyRotateDialog } from "@/src/components/api-keys/api-key-rotate-dialog";
import type { ApiKeySecretViewHandle } from "@/src/components/api-keys/api-key-secret-view";
import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/features/application/ui/interaction-readiness";
import { Badge } from "@/src/components/ui/badge";
import { Button } from "@/src/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/src/components/ui/table";
import type { ApiKeyOwner } from "@/src/features/api-keys/api-key-routes";
import type { ApiKeyMutationArbiter } from "@/src/features/api-keys/api-key-mutation-arbiter";
import type { ApiKeyResponse } from "@/src/lib/api/generated/types.gen";

function formattedDate(value: string | null, locale: string, fallback: string) {
  if (!value) return fallback;
  const date = new Date(value);
  return Number.isNaN(date.valueOf())
    ? value
    : new Intl.DateTimeFormat(locale, {
        dateStyle: "medium",
        timeZone: "UTC",
      }).format(date);
}

export function ApiKeyTable({
  apiKeys,
  busyKeyIds,
  mutationArbiter,
  onConfirmed,
  onRevoked,
  onToggle,
  owner,
  secretViewRef,
}: Readonly<{
  apiKeys: readonly ApiKeyResponse[];
  busyKeyIds: ReadonlySet<string>;
  mutationArbiter: ApiKeyMutationArbiter;
  onConfirmed: (apiKey: ApiKeyResponse, action: "updated" | "rotated") => void;
  onRevoked: (apiKeyId: string) => void;
  onToggle: (apiKey: ApiKeyResponse) => void;
  owner: ApiKeyOwner;
  secretViewRef: RefObject<ApiKeySecretViewHandle | null>;
}>) {
  const t = useTranslations("apiKeys");
  const locale = useLocale();
  const interactionReady = useInteractionReady();

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>{t("list.columns.key")}</TableHead>
          <TableHead>{t("list.columns.status")}</TableHead>
          <TableHead>{t("list.columns.access")}</TableHead>
          <TableHead>{t("list.columns.rateLimit")}</TableHead>
          <TableHead>{t("list.columns.expires")}</TableHead>
          <TableHead>{t("list.columns.lastUsed")}</TableHead>
          <TableHead>{t("list.columns.created")}</TableHead>
          <TableHead>{t("list.columns.actions")}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {apiKeys.map((apiKey) => {
          const mutationBusy = busyKeyIds.has(apiKey.id);
          return (
            <TableRow key={apiKey.id}>
              <TableCell>
                <div className="flex min-w-40 flex-col gap-1">
                  <strong>{apiKey.name}</strong>
                  <code className="text-muted-foreground">{apiKey.start}</code>
                  <span className="sr-only">
                    {t("list.safeStart", { start: apiKey.start })}
                  </span>
                </div>
              </TableCell>
              <TableCell>
                <Badge
                  variant={apiKey.status === "active" ? "default" : "outline"}
                >
                  {t(`statuses.${apiKey.status}`)}
                </Badge>
              </TableCell>
              <TableCell>
                <ul className="flex min-w-40 flex-col gap-1">
                  {apiKey.scopes.map((scope) => (
                    <li key={scope}>{t(`scopes.${scope}`)}</li>
                  ))}
                </ul>
              </TableCell>
              <TableCell>
                <div className="flex min-w-36 flex-col gap-1">
                  <span>
                    {apiKey.rateLimitEnabled
                      ? t("list.rateEnabled", {
                          max: apiKey.rateLimitMax,
                          window: t(`rateWindow.${apiKey.rateLimitWindow}`),
                        })
                      : t("list.rateDisabled")}
                  </span>
                  {apiKey.rateLimitEnabled ? (
                    <span className="text-muted-foreground">
                      {t("list.requestCount", { count: apiKey.requestCount })}
                    </span>
                  ) : null}
                </div>
              </TableCell>
              <TableCell>
                <time dateTime={apiKey.expiresAt ?? undefined}>
                  {formattedDate(apiKey.expiresAt, locale, t("list.never"))}
                </time>
              </TableCell>
              <TableCell>
                <time dateTime={apiKey.lastRequestAt ?? undefined}>
                  {formattedDate(
                    apiKey.lastRequestAt,
                    locale,
                    t("list.notYet"),
                  )}
                </time>
              </TableCell>
              <TableCell>
                <time dateTime={apiKey.createdAt}>
                  {formattedDate(apiKey.createdAt, locale, apiKey.createdAt)}
                </time>
              </TableCell>
              <TableCell>
                <div className="flex min-w-52 flex-wrap gap-2">
                  <ApiKeyEditDialog
                    apiKey={apiKey}
                    mutationArbiter={mutationArbiter}
                    mutationBusy={mutationBusy}
                    onConfirmed={(confirmed) =>
                      onConfirmed(confirmed, "updated")
                    }
                    owner={owner}
                  />
                  <Button
                    {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
                    disabled={!interactionReady || mutationBusy}
                    onClick={() => onToggle(apiKey)}
                    size="sm"
                    type="button"
                    variant="outline"
                  >
                    {apiKey.enabled
                      ? t("actions.disable")
                      : t("actions.enable")}
                  </Button>
                  <ApiKeyRotateDialog
                    apiKey={apiKey}
                    mutationArbiter={mutationArbiter}
                    mutationBusy={mutationBusy}
                    onConfirmed={(confirmed) =>
                      onConfirmed(confirmed, "rotated")
                    }
                    owner={owner}
                    secretViewRef={secretViewRef}
                  />
                  <ApiKeyRevokeDialog
                    apiKey={apiKey}
                    mutationArbiter={mutationArbiter}
                    mutationBusy={mutationBusy}
                    onConfirmed={onRevoked}
                    owner={owner}
                  />
                </div>
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}
