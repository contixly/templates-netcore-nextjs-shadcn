import {
  IconClock,
  IconEdit,
  IconEyeOff,
  IconFolder,
  IconGitBranch,
  IconLanguage,
  IconTarget,
  IconUser,
} from "@tabler/icons-react";
import { useTranslations } from "next-intl";
import type { CSSProperties, ReactNode } from "react";

import type {
  DocumentInfo,
  DocumentStatus,
} from "@/src/features/documents/documents-types";
import { cn } from "@/src/lib/utils";

const STATUS_DOT_CLASS: Record<DocumentStatus, string> = {
  draft: "bg-amber-500",
  review: "bg-sky-500",
  published: "bg-emerald-500",
  archived: "bg-muted-foreground/40",
};

const META_CELL_CLASS = "flex min-w-0 flex-col gap-1 bg-muted/40 px-4 py-3";

function MetaItem({
  children,
  className,
  icon,
  label,
  valueClassName,
}: Readonly<{
  children: ReactNode;
  className?: string;
  icon: ReactNode;
  label: string;
  valueClassName?: string;
}>) {
  return (
    <div className={cn(META_CELL_CLASS, className)}>
      <span className="text-[10.5px] font-medium tracking-[0.06em] text-muted-foreground uppercase">
        {label}
      </span>
      <span className="flex min-w-0 items-center gap-1.5 text-[13px] font-medium text-foreground">
        <span className="flex size-3.5 shrink-0 items-center justify-center text-muted-foreground">
          {icon}
        </span>
        <span className={cn(valueClassName ?? "truncate")}>{children}</span>
      </span>
    </div>
  );
}

export function DocumentsPageMeta({
  document,
}: Readonly<{ document: DocumentInfo }>) {
  const t = useTranslations("documents.meta");
  const { meta } = document;
  const showStatus = meta.status !== "published";
  const secondaryItems: ReactNode[] = [];

  if (meta.editedAt) {
    secondaryItems.push(
      <MetaItem
        icon={<IconEdit size={14} />}
        key="edited-at"
        label={t("editedAt")}
      >
        <time dateTime={meta.editedAt}>{meta.editedAt}</time>
      </MetaItem>,
    );
  }

  if (meta.author) {
    secondaryItems.push(
      <MetaItem icon={<IconUser size={14} />} key="author" label={t("author")}>
        {meta.author}
      </MetaItem>,
    );
  }

  if (meta.version) {
    secondaryItems.push(
      <MetaItem
        icon={<IconGitBranch size={14} />}
        key="version"
        label={t("version")}
      >
        <span className="font-mono">v{meta.version}</span>
      </MetaItem>,
    );
  }

  if (meta.reading) {
    secondaryItems.push(
      <MetaItem
        icon={<IconClock size={14} />}
        key="reading"
        label={t("reading")}
      >
        {meta.reading}
      </MetaItem>,
    );
  }

  if (showStatus) {
    secondaryItems.push(
      <div className={META_CELL_CLASS} key="status">
        <span className="text-[10.5px] font-medium tracking-[0.06em] text-muted-foreground uppercase">
          {t(`status.${meta.status}`)}
        </span>
        <span className="inline-flex w-fit items-center gap-1.5 rounded-full border bg-background px-2 py-0.5 text-[11px] font-medium tracking-wide text-foreground uppercase">
          <span
            className={cn(
              "size-1.5 rounded-full",
              STATUS_DOT_CLASS[meta.status],
            )}
          />
          {t(`status.${meta.status}`)}
        </span>
      </div>,
    );
  }

  if (meta.hide) {
    secondaryItems.push(
      <MetaItem
        icon={<IconEyeOff size={14} />}
        key="visibility"
        label={t("visibility.visible")}
        valueClassName="whitespace-normal break-words"
      >
        {t("visibility.hidden")}
      </MetaItem>,
    );
  }

  if (document.isLocaleFallback) {
    secondaryItems.push(
      <MetaItem
        icon={<IconLanguage size={14} />}
        key="language"
        label={t("language.en")}
        valueClassName="whitespace-normal break-words"
      >
        {t(`language.${document.contentLocale}`)} ·{" "}
        {t(`fallback.${document.contentLocale}`)}
      </MetaItem>,
    );
  }

  return (
    <div
      className="mt-5 overflow-hidden rounded-sm border border-muted-foreground/50 bg-border shadow-xs"
      data-hidden-in-production={meta.hide ? "true" : "false"}
      data-status-tone={showStatus ? meta.status : "default"}
    >
      <div className="grid grid-cols-1 gap-px bg-border sm:grid-cols-2">
        <MetaItem
          className={!meta.purpose ? "sm:col-span-2" : undefined}
          icon={<IconFolder size={14} />}
          label={t("group")}
          valueClassName="whitespace-normal break-words"
        >
          {meta.group} / {meta.parentItem}
        </MetaItem>
        {meta.purpose ? (
          <MetaItem
            icon={<IconTarget size={14} />}
            label={t("purpose")}
            valueClassName="whitespace-normal break-words"
          >
            {meta.purpose}
          </MetaItem>
        ) : null}
      </div>
      {secondaryItems.length > 0 ? (
        <div
          className="mt-px grid grid-cols-2 gap-px bg-border sm:grid-cols-3 lg:[grid-template-columns:repeat(var(--documents-meta-secondary-count),minmax(0,1fr))]"
          style={
            {
              "--documents-meta-secondary-count": secondaryItems.length,
            } as CSSProperties
          }
        >
          {secondaryItems}
        </div>
      ) : null}
    </div>
  );
}
