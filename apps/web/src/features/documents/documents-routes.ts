import type { Route } from "next";

import { resolveAppLocale, type AppLocale } from "@/src/i18n/config";

import { getDocumentsRegistry } from "./documents-registry";

const LOCALE_SUFFIX_PATTERN = /\.(?:en|ru)$/u;

export const documentsRoutes = {
  root: "/docs" as Route,
  document(canonicalUrl: string): Route {
    return (
      canonicalUrl === "index" ? "/docs" : `/docs/${canonicalUrl}`
    ) as Route;
  },
} as const;

export function resolveDocumentsLocale(
  value = process.env.PUBLIC_DEFAULT_LOCALE,
): AppLocale {
  return resolveAppLocale(value);
}

export function canonicalDocumentUrlFromSlug(slug: string[]): string {
  return slug.join("/");
}

export function buildDocumentStaticParams(): Array<{ slug: string[] }> {
  return getDocumentsRegistry(resolveDocumentsLocale())
    .visibleDocuments.filter((document) => document.canonicalUrl !== "index")
    .map((document) => {
      if (
        document.slug.some((segment) => LOCALE_SUFFIX_PATTERN.test(segment))
      ) {
        throw new Error(
          "Documentation routes must not contain locale suffixes.",
        );
      }

      return { slug: [...document.slug] };
    });
}
