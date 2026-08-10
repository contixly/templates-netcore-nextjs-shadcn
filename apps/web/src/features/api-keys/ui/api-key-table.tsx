"use client";

import { useState, type RefObject } from "react";
import { useLocale, useTranslations } from "next-intl";
import {
  IconDotsVertical,
  IconPencil,
  IconPower,
  IconRefresh,
  IconTrash,
} from "@tabler/icons-react";

import { Badge } from "@/src/components/ui/badge";
import { Button } from "@/src/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/src/components/ui/dropdown-menu";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/src/components/ui/table";
import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/features/application/ui/interaction-readiness";
import type { ApiKeyMutationArbiter } from "@/src/features/api-keys/api-key-mutation-arbiter";
import type { ApiKeyOwner } from "@/src/features/api-keys/api-key-routes";
import { ApiKeyEditDialog } from "@/src/features/api-keys/ui/api-key-edit-dialog";
import { ApiKeyPermissionsPreview } from "@/src/features/api-keys/ui/api-key-permissions-preview";
import { ApiKeyRevokeDialog } from "@/src/features/api-keys/ui/api-key-revoke-dialog";
import { ApiKeyRotateDialog } from "@/src/features/api-keys/ui/api-key-rotate-dialog";
import type { ApiKeySecretViewHandle } from "@/src/features/api-keys/ui/api-key-secret-view";
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

function statusVariant(status: ApiKeyResponse["status"]) {
  if (status === "active") return "secondary" as const;
  if (status === "expired") return "destructive" as const;
  return "outline" as const;
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
  const [openMenuId, setOpenMenuId] = useState<string | null>(null);

  return (
    <Table className="min-w-[72rem]">
      <TableHeader>
        <TableRow>
          <TableHead>{t("list.columns.name")}</TableHead>
          <TableHead>{t("list.columns.key")}</TableHead>
          <TableHead>{t("list.columns.access")}</TableHead>
          <TableHead>{t("list.columns.rateLimit")}</TableHead>
          <TableHead>{t("list.columns.expires")}</TableHead>
          <TableHead>{t("list.columns.lastUsed")}</TableHead>
          <TableHead>{t("list.columns.created")}</TableHead>
          <TableHead className="w-12 text-right">
            {t("list.columns.actions")}
          </TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {apiKeys.map((apiKey) => {
          const mutationBusy = busyKeyIds.has(apiKey.id);
          return (
            <TableRow key={apiKey.id}>
              <TableCell className="min-w-44">
                <div className="flex min-w-0 flex-col gap-1">
                  <span className="truncate font-medium">{apiKey.name}</span>
                  <Badge
                    className="w-fit"
                    variant={statusVariant(apiKey.status)}
                  >
                    {t(`statuses.${apiKey.status}`)}
                  </Badge>
                </div>
              </TableCell>
              <TableCell>
                <code className="bg-muted px-1.5 py-1 text-xs text-muted-foreground">
                  {apiKey.start}
                </code>
                <span className="sr-only">
                  {t("list.safeStart", { start: apiKey.start })}
                </span>
              </TableCell>
              <TableCell className="min-w-56 whitespace-normal">
                <ApiKeyPermissionsPreview
                  emptyLabel={t("presets.noScopes")}
                  scopes={apiKey.scopes}
                />
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
              <TableCell className="text-right">
                <DropdownMenu
                  onOpenChange={(open) =>
                    setOpenMenuId(open ? apiKey.id : null)
                  }
                  open={openMenuId === apiKey.id}
                >
                  <DropdownMenuTrigger asChild>
                    <Button
                      {...{
                        [INTERACTION_READY_ATTRIBUTE]: interactionReady,
                      }}
                      aria-label={t("actions.more", { name: apiKey.name })}
                      disabled={!interactionReady || mutationBusy}
                      onClick={() =>
                        setOpenMenuId((current) =>
                          current === apiKey.id ? null : apiKey.id,
                        )
                      }
                      size="icon-sm"
                      type="button"
                      variant="ghost"
                    >
                      <IconDotsVertical />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuGroup>
                      <ApiKeyEditDialog
                        apiKey={apiKey}
                        mutationArbiter={mutationArbiter}
                        mutationBusy={mutationBusy}
                        onClosed={() => setOpenMenuId(null)}
                        onConfirmed={(confirmed) =>
                          onConfirmed(confirmed, "updated")
                        }
                        owner={owner}
                        trigger={
                          <DropdownMenuItem
                            disabled={!interactionReady || mutationBusy}
                            onSelect={(event) => event.preventDefault()}
                          >
                            <IconPencil />
                            {t("actions.edit")}
                          </DropdownMenuItem>
                        }
                      />
                      <DropdownMenuItem
                        disabled={!interactionReady || mutationBusy}
                        onSelect={() => onToggle(apiKey)}
                      >
                        <IconPower />
                        {apiKey.enabled
                          ? t("actions.disable")
                          : t("actions.enable")}
                      </DropdownMenuItem>
                      <ApiKeyRotateDialog
                        apiKey={apiKey}
                        mutationArbiter={mutationArbiter}
                        mutationBusy={mutationBusy}
                        onClosed={() => setOpenMenuId(null)}
                        onConfirmed={(confirmed) =>
                          onConfirmed(confirmed, "rotated")
                        }
                        owner={owner}
                        secretViewRef={secretViewRef}
                        trigger={
                          <DropdownMenuItem
                            disabled={!interactionReady || mutationBusy}
                            onSelect={(event) => event.preventDefault()}
                          >
                            <IconRefresh />
                            {t("actions.rotate")}
                          </DropdownMenuItem>
                        }
                      />
                      <ApiKeyRevokeDialog
                        apiKey={apiKey}
                        mutationArbiter={mutationArbiter}
                        mutationBusy={mutationBusy}
                        onClosed={() => setOpenMenuId(null)}
                        onConfirmed={onRevoked}
                        owner={owner}
                        trigger={
                          <DropdownMenuItem
                            disabled={!interactionReady || mutationBusy}
                            onSelect={(event) => event.preventDefault()}
                            variant="destructive"
                          >
                            <IconTrash />
                            {t("actions.revoke")}
                          </DropdownMenuItem>
                        }
                      />
                    </DropdownMenuGroup>
                  </DropdownMenuContent>
                </DropdownMenu>
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}
