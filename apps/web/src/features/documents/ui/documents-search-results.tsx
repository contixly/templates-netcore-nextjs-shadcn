"use client";

import { IconFileText, IconTextCaption } from "@tabler/icons-react";
import { useTranslations } from "next-intl";
import type { ReactNode } from "react";

import {
  CommandGroup,
  CommandItem,
  CommandList,
  CommandSeparator,
  CommandShortcut,
} from "@/src/components/ui/command";
import type { DocumentSearchResponse } from "@/src/lib/api/generated/types.gen";

export type DocumentsSearchStatus = "idle" | "loading" | "success" | "error";

function ResultGroup({
  children,
  label,
}: Readonly<{ children: ReactNode; label: string }>) {
  return (
    <CommandGroup className="px-1 pb-1" heading={label}>
      {children}
    </CommandGroup>
  );
}

export function DocumentsSearchResults({
  onSelect,
  query,
  results,
  status,
}: Readonly<{
  onSelect: (href: string) => void;
  query: string;
  results: DocumentSearchResponse;
  status: DocumentsSearchStatus;
}>) {
  const t = useTranslations("documents.search");
  const blocked = status === "loading";
  const hasResults = results.pages.length + results.headings.length > 0;

  return (
    <CommandList
      aria-busy={blocked}
      className="max-h-[min(28rem,calc(100vh-12rem))] overflow-x-hidden overflow-y-auto p-1 outline-none"
      label={t("results")}
    >
      {status === "error" ? (
        <p className="px-4 py-8 text-sm text-muted-foreground" role="alert">
          {t("unavailable")}
        </p>
      ) : null}

      {status === "loading" && !hasResults ? (
        <p
          aria-live="polite"
          className="px-4 py-8 text-sm text-muted-foreground"
          role="status"
        >
          {t("loading")}
        </p>
      ) : null}

      {status === "success" && !hasResults ? (
        <p
          aria-live="polite"
          className="px-4 py-8 text-sm text-muted-foreground"
          role="status"
        >
          {query.trim() ? t("emptyResults") : t("emptyQuery")}
        </p>
      ) : null}

      {blocked && hasResults ? (
        <p aria-live="polite" className="sr-only" role="status">
          {t("loading")}
        </p>
      ) : null}

      {results.pages.length > 0 ? (
        <ResultGroup label={t("pages")}>
          {results.pages.map((page) => (
            <CommandItem
              className="items-start"
              disabled={blocked}
              key={page.href}
              onSelect={() => onSelect(page.href)}
              value={page.href}
            >
              <IconFileText aria-hidden="true" className="mt-0.5" />
              <span className="min-w-0 flex-1">
                <span className="block truncate font-medium">{page.title}</span>
                <span className="block truncate text-xs text-muted-foreground">
                  {page.group} · {page.parentItem}
                </span>
                <span className="block truncate text-xs text-muted-foreground">
                  {page.description}
                </span>
              </span>
              <CommandShortcut aria-hidden="true">↵</CommandShortcut>
            </CommandItem>
          ))}
        </ResultGroup>
      ) : null}

      {results.headings.length > 0 ? (
        <>
          {results.pages.length > 0 ? (
            <CommandSeparator className="my-1" />
          ) : null}
          <ResultGroup label={t("headings")}>
            {results.headings.map((heading) => (
              <CommandItem
                className="items-start"
                disabled={blocked}
                key={heading.href}
                onSelect={() => onSelect(heading.href)}
                value={heading.href}
              >
                <IconTextCaption aria-hidden="true" className="mt-0.5" />
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-medium">
                    {heading.title}
                  </span>
                  <span className="block truncate text-xs text-muted-foreground">
                    {heading.pageTitle}
                  </span>
                </span>
                <CommandShortcut aria-hidden="true">↵</CommandShortcut>
              </CommandItem>
            ))}
          </ResultGroup>
        </>
      ) : null}
    </CommandList>
  );
}
