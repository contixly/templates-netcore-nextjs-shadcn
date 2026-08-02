"use client";

import { IconSearch } from "@tabler/icons-react";
import type { Route } from "next";
import { useRouter } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useRef, useState } from "react";

import {
  DocumentsSearchResults,
  type DocumentsSearchStatus,
} from "@/src/components/documents/documents-search-results";
import { Button } from "@/src/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/src/components/ui/dialog";
import { Input } from "@/src/components/ui/input";
import { resolveAppLocale } from "@/src/i18n/config";
import { searchDocuments } from "@/src/lib/api/documents/browser/search-documents";
import type { DocumentSearchResponse } from "@/src/lib/api/generated/types.gen";

const SEARCH_DEBOUNCE_MS = 250;
const emptyResults: DocumentSearchResponse = { pages: [], headings: [] };

type SearchState = Readonly<{
  results: DocumentSearchResponse;
  status: DocumentsSearchStatus;
}>;

const idleState: SearchState = { results: emptyResults, status: "idle" };

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === "AbortError";
}

export function DocumentsSearch() {
  const router = useRouter();
  const locale = resolveAppLocale(useLocale());
  const t = useTranslations("documents.search");
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [searchState, setSearchState] = useState<SearchState>(idleState);
  const controllerRef = useRef<AbortController | null>(null);
  const generationRef = useRef(0);

  const cancelActiveRequest = useCallback(() => {
    generationRef.current += 1;
    controllerRef.current?.abort();
    controllerRef.current = null;
  }, []);

  const resetAndClose = useCallback(() => {
    cancelActiveRequest();
    setOpen(false);
    setQuery("");
    setSearchState(idleState);
  }, [cancelActiveRequest]);

  const handleOpenChange = useCallback(
    (nextOpen: boolean) => {
      if (!nextOpen) {
        resetAndClose();
        return;
      }

      setOpen(true);
      setSearchState({ results: emptyResults, status: "loading" });
    },
    [resetAndClose],
  );

  useEffect(() => {
    const handleShortcut = (event: KeyboardEvent) => {
      if (
        event.key.toLowerCase() !== "k" ||
        (!event.ctrlKey && !event.metaKey)
      ) {
        return;
      }

      event.preventDefault();
      if (open) {
        resetAndClose();
      } else {
        handleOpenChange(true);
      }
    };

    document.addEventListener("keydown", handleShortcut);
    return () => document.removeEventListener("keydown", handleShortcut);
  }, [handleOpenChange, open, resetAndClose]);

  useEffect(() => {
    if (!open) {
      return;
    }

    controllerRef.current?.abort();
    const controller = new AbortController();
    const generation = generationRef.current + 1;
    generationRef.current = generation;
    controllerRef.current = controller;

    const isCurrentRequest = () =>
      !controller.signal.aborted && generation === generationRef.current;

    const timer = window.setTimeout(() => {
      void (async () => {
        try {
          const result = await searchDocuments({
            query,
            locale,
            signal: controller.signal,
          });

          if (!isCurrentRequest()) {
            return;
          }

          setSearchState(
            result.ok
              ? { results: result.data, status: "success" }
              : { results: emptyResults, status: "error" },
          );
        } catch (error) {
          if (!isCurrentRequest() || isAbortError(error)) {
            return;
          }

          setSearchState({ results: emptyResults, status: "error" });
        }
      })();
    }, SEARCH_DEBOUNCE_MS);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
      if (controllerRef.current === controller) {
        controllerRef.current = null;
      }
    };
  }, [locale, open, query]);

  const handleQueryChange = (value: string) => {
    setQuery(value);
    setSearchState((current) => ({ ...current, status: "loading" }));
  };

  const handleSelect = (href: string) => {
    resetAndClose();
    router.push(href as Route);
  };

  return (
    <Dialog onOpenChange={handleOpenChange} open={open}>
      <DialogTrigger asChild>
        <Button aria-label={t("open")} type="button" variant="outline">
          <IconSearch aria-hidden="true" data-icon="inline-start" />
          <span className="hidden sm:inline">{t("open")}</span>
          <kbd
            aria-hidden="true"
            className="hidden border bg-muted px-1 text-[0.65rem] text-muted-foreground sm:inline"
          >
            Ctrl/⌘ K
          </kbd>
        </Button>
      </DialogTrigger>

      <DialogContent
        className="gap-0 overflow-hidden p-0 sm:max-w-2xl"
        showCloseButton={false}
      >
        <DialogHeader className="sr-only">
          <DialogTitle>{t("title")}</DialogTitle>
          <DialogDescription>{t("description")}</DialogDescription>
        </DialogHeader>

        <div className="flex items-center gap-2 border-b p-3">
          <IconSearch aria-hidden="true" />
          <Input
            aria-label={t("open")}
            autoComplete="off"
            autoFocus
            className="border-0 p-0 focus-visible:ring-0"
            maxLength={120}
            onChange={(event) => handleQueryChange(event.currentTarget.value)}
            placeholder={t("placeholder")}
            type="search"
            value={query}
          />
        </div>

        <DocumentsSearchResults
          onSelect={handleSelect}
          query={query}
          results={searchState.results}
          status={searchState.status}
        />
      </DialogContent>
    </Dialog>
  );
}
