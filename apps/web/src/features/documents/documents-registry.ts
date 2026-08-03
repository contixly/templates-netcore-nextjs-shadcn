import type { ComponentType } from "react";
import type { MDXProps } from "mdx/types";

import { locales, type AppLocale } from "@/src/i18n/config";

import { documentModules, documents } from "./generated/documents-registry.gen";
import type {
  DocumentHeading,
  DocumentInfo,
  DocumentMetadata,
  DocumentModule,
  DocumentsRegistry,
} from "./documents-types";

type GeneratedDocumentRecord = {
  sourcePath: string;
  canonicalSourcePath: string;
  canonicalUrl: string;
  contentLocale: AppLocale;
  hasExplicitLocale: boolean;
  availableLocales: readonly AppLocale[];
  slug: readonly string[];
  href: string;
  headings: readonly DocumentHeading[];
  meta: DocumentMetadata;
};

const generatedDocuments =
  documents as unknown as readonly GeneratedDocumentRecord[];
const registryCache = new Map<AppLocale, DocumentsRegistry>();

function compareText(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

function resolveGroupOrders(documentRecords: DocumentInfo[]) {
  const groupOrders = new Map<string, number>();
  const parentOrders = new Map<string, number>();

  for (const document of documentRecords) {
    const { group, groupOrder, parentItem, parentItemOrder } = document.meta;
    const parentKey = `${group}\u0000${parentItem}`;

    if (groupOrder !== undefined) {
      groupOrders.set(
        group,
        Math.max(groupOrders.get(group) ?? -Infinity, groupOrder),
      );
    }
    if (parentItemOrder !== undefined) {
      parentOrders.set(
        parentKey,
        Math.max(parentOrders.get(parentKey) ?? -Infinity, parentItemOrder),
      );
    }
  }

  return { groupOrders, parentOrders };
}

function sortDocuments(documentRecords: DocumentInfo[]): DocumentInfo[] {
  const { groupOrders, parentOrders } = resolveGroupOrders(documentRecords);

  return [...documentRecords].sort((left, right) => {
    const groupOrder =
      (groupOrders.get(right.meta.group) ?? 0) -
      (groupOrders.get(left.meta.group) ?? 0);
    if (groupOrder !== 0) return groupOrder;

    const group = compareText(left.meta.group, right.meta.group);
    if (group !== 0) return group;

    const leftParentKey = `${left.meta.group}\u0000${left.meta.parentItem}`;
    const rightParentKey = `${right.meta.group}\u0000${right.meta.parentItem}`;
    const parentOrder =
      (parentOrders.get(rightParentKey) ?? 0) -
      (parentOrders.get(leftParentKey) ?? 0);
    if (parentOrder !== 0) return parentOrder;

    const parent = compareText(left.meta.parentItem, right.meta.parentItem);
    if (parent !== 0) return parent;

    const order = right.meta.order - left.meta.order;
    if (order !== 0) return order;

    const title = compareText(left.meta.title, right.meta.title);
    if (title !== 0) return title;

    return compareText(left.canonicalUrl, right.canonicalUrl);
  });
}

function selectDocumentVariant(
  variants: readonly GeneratedDocumentRecord[],
  locale: AppLocale,
): GeneratedDocumentRecord {
  const selected =
    variants.find((variant) => variant.contentLocale === locale) ??
    locales
      .map((candidateLocale) =>
        variants.find((variant) => variant.contentLocale === candidateLocale),
      )
      .find(
        (variant): variant is GeneratedDocumentRecord => variant !== undefined,
      );

  if (!selected) {
    throw new Error(
      "A generated documentation route has no localized content variant.",
    );
  }

  return selected;
}

function toDocumentInfo(
  variant: GeneratedDocumentRecord,
  locale: AppLocale,
): DocumentInfo {
  return {
    sourcePath: variant.sourcePath,
    canonicalSourcePath: variant.canonicalSourcePath,
    canonicalUrl: variant.canonicalUrl,
    requestedLocale: locale,
    contentLocale: variant.contentLocale,
    isLocaleFallback: variant.contentLocale !== locale,
    hasExplicitLocale: variant.hasExplicitLocale,
    availableLocales: [...variant.availableLocales],
    slug: [...variant.slug],
    href: variant.href,
    headings: variant.headings.map((heading) => ({ ...heading })),
    meta: { ...variant.meta },
  };
}

export function isProductionVisibleDocument(document: DocumentInfo): boolean {
  return (
    document.meta.hide !== true &&
    (document.meta.status === "published" ||
      document.meta.status === "archived")
  );
}

export function getDocumentsRegistry(locale: AppLocale): DocumentsRegistry {
  const cached = registryCache.get(locale);
  if (cached) return cached;

  const variantsByCanonicalUrl = new Map<string, GeneratedDocumentRecord[]>();
  for (const variant of generatedDocuments) {
    const variants = variantsByCanonicalUrl.get(variant.canonicalUrl) ?? [];
    variants.push(variant);
    variantsByCanonicalUrl.set(variant.canonicalUrl, variants);
  }

  const allDocuments = sortDocuments(
    [...variantsByCanonicalUrl.values()].map((variants) =>
      toDocumentInfo(selectDocumentVariant(variants, locale), locale),
    ),
  );
  const registry = {
    locale,
    allDocuments,
    visibleDocuments: allDocuments.filter(isProductionVisibleDocument),
  } satisfies DocumentsRegistry;

  registryCache.set(locale, registry);
  return registry;
}

export function findPublishedDocument(
  locale: AppLocale,
  canonicalUrl: string,
): DocumentInfo | undefined {
  return getDocumentsRegistry(locale).visibleDocuments.find(
    (document) => document.canonicalUrl === canonicalUrl,
  );
}

export async function importDocument(
  document: DocumentInfo,
): Promise<{ default: ComponentType<MDXProps> }> {
  const importer =
    documentModules[document.sourcePath as keyof typeof documentModules];

  if (!importer) {
    throw new Error("The generated documentation module is unavailable.");
  }

  return importer() as Promise<DocumentModule>;
}
