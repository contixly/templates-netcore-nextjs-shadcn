"use client";

import { IconSearch } from "@tabler/icons-react";
import type { Route } from "next";
import { useRouter } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useRef, useState } from "react";

import {
  DocumentsSearchResults,
  type DocumentsSearchStatus,
} from "@/src/features/documents/ui/documents-search-results";
import { Button } from "@/src/components/ui/button";
import { Command, CommandInput } from "@/src/components/ui/command";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/src/components/ui/dialog";
import { Kbd, KbdGroup } from "@/src/components/ui/kbd";
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
        <Button
          aria-label={t("open")}
          className="h-9 w-9 justify-center px-0 text-muted-foreground sm:w-auto sm:min-w-24 sm:px-3 xl:w-[min(22rem,calc(100vw-8rem))] xl:justify-start"
          type="button"
          variant="outline"
        >
          <IconSearch
            aria-hidden="true"
            className="sm:hidden xl:block"
            data-icon="inline-start"
          />
          <span className="hidden min-w-0 flex-1 truncate text-left xl:block">
            {t("placeholder")}
          </span>
          <KbdGroup
            aria-hidden="true"
            className="hidden sm:inline-flex xl:ml-auto"
          >
            <Kbd>Ctrl/⌘</Kbd>
            <Kbd>K</Kbd>
          </KbdGroup>
        </Button>
      </DialogTrigger>

      <DialogContent
        className="top-1/3 gap-0 overflow-hidden p-0 sm:max-w-2xl"
        showCloseButton={false}
      >
        <DialogHeader className="sr-only">
          <DialogTitle>{t("title")}</DialogTitle>
          <DialogDescription>{t("description")}</DialogDescription>
        </DialogHeader>

        <Command label={t("open")} loop shouldFilter={false}>
          <CommandInput
            aria-label={t("open")}
            autoFocus
            className="h-12 px-3 text-sm"
            maxLength={120}
            onValueChange={handleQueryChange}
            placeholder={t("placeholder")}
            value={query}
          />

          <DocumentsSearchResults
            onSelect={handleSelect}
            query={query}
            results={searchState.results}
            status={searchState.status}
          />
        </Command>
      </DialogContent>
    </Dialog>
  );
}
