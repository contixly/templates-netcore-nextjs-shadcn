"use client";

import { IconFileText, IconTextCaption } from "@tabler/icons-react";
import { useTranslations } from "next-intl";
import { useId, type ReactNode } from "react";

import type { DocumentSearchResponse } from "@/src/lib/api/generated/types.gen";

export type DocumentsSearchStatus = "idle" | "loading" | "success" | "error";

function ResultGroup({
  children,
  label,
}: Readonly<{ children: ReactNode; label: string }>) {
  const labelId = useId();

  return (
    <div aria-labelledby={labelId} role="group">
      <h3
        className="px-2 py-1.5 text-xs font-medium text-muted-foreground"
        id={labelId}
      >
        {label}
      </h3>
      <div className="space-y-0 px-1 pb-1">{children}</div>
    </div>
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

  if (status === "error") {
    return (
      <p className="px-4 py-8 text-sm text-muted-foreground" role="alert">
        {t("unavailable")}
      </p>
    );
  }

  if (status === "loading" && !hasResults) {
    return (
      <p
        aria-live="polite"
        className="px-4 py-8 text-sm text-muted-foreground"
        role="status"
      >
        {t("loading")}
      </p>
    );
  }

  if (status === "success" && !hasResults) {
    return (
      <p
        aria-live="polite"
        className="px-4 py-8 text-sm text-muted-foreground"
        role="status"
      >
        {query.trim() ? t("emptyResults") : t("emptyQuery")}
      </p>
    );
  }

  if (!hasResults) {
    return null;
  }

  return (
    <div
      aria-busy={blocked}
      aria-label={t("results")}
      className="max-h-[min(28rem,calc(100vh-12rem))] overflow-x-hidden overflow-y-auto p-1 outline-none"
      role="listbox"
    >
      {blocked ? (
        <p aria-live="polite" className="sr-only" role="status">
          {t("loading")}
        </p>
      ) : null}

      {results.pages.length > 0 ? (
        <ResultGroup label={t("pages")}>
          {results.pages.map((page) => (
            <button
              aria-selected="false"
              className="flex w-full cursor-default items-start gap-2 rounded-none px-2 py-2 text-left text-xs outline-none select-none hover:bg-muted focus-visible:bg-muted disabled:cursor-wait disabled:opacity-50"
              disabled={blocked}
              key={page.href}
              onClick={() => onSelect(page.href)}
              role="option"
              type="button"
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
              <span
                aria-hidden="true"
                className="ml-auto text-xs tracking-widest text-muted-foreground"
              >
                ↵
              </span>
            </button>
          ))}
        </ResultGroup>
      ) : null}

      {results.headings.length > 0 ? (
        <>
          {results.pages.length > 0 ? (
            <div className="my-1 h-px bg-border" />
          ) : null}
          <ResultGroup label={t("headings")}>
            {results.headings.map((heading) => (
              <button
                aria-selected="false"
                className="flex w-full cursor-default items-start gap-2 rounded-none px-2 py-2 text-left text-xs outline-none select-none hover:bg-muted focus-visible:bg-muted disabled:cursor-wait disabled:opacity-50"
                disabled={blocked}
                key={heading.href}
                onClick={() => onSelect(heading.href)}
                role="option"
                type="button"
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
                <span
                  aria-hidden="true"
                  className="ml-auto text-xs tracking-widest text-muted-foreground"
                >
                  ↵
                </span>
              </button>
            ))}
          </ResultGroup>
        </>
      ) : null}
    </div>
  );
}
