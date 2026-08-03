import type { ComponentType } from "react";
import type { MDXProps } from "mdx/types";

import type { AppLocale } from "@/src/i18n/config";

export type DocumentStatus = "draft" | "review" | "published" | "archived";

export type DocumentHeading = {
  level: 2 | 3;
  title: string;
  id: string;
};

export type DocumentMetadata = {
  title: string;
  description: string;
  group: string;
  groupOrder?: number;
  parentItem: string;
  parentItemOrder?: number;
  order: number;
  status: DocumentStatus;
  hide?: boolean;
  toc: boolean;
  purpose?: string;
  author?: string;
  version?: string;
  editedAt?: string;
  reading?: string;
  source?: string;
};

export type DocumentInfo = {
  sourcePath: string;
  canonicalSourcePath: string;
  canonicalUrl: string;
  requestedLocale: AppLocale;
  contentLocale: AppLocale;
  isLocaleFallback: boolean;
  hasExplicitLocale: boolean;
  availableLocales: AppLocale[];
  slug: string[];
  href: string;
  headings: DocumentHeading[];
  meta: DocumentMetadata;
};

export type DocumentModule = {
  default: ComponentType<MDXProps>;
};

export type DocumentsRegistry = {
  locale: AppLocale;
  allDocuments: DocumentInfo[];
  visibleDocuments: DocumentInfo[];
};

export type DocumentNavigationItem = Pick<
  DocumentInfo,
  "canonicalUrl" | "href"
> & {
  title: string;
  description: string;
};

export type DocumentPageNavigation = {
  previous?: DocumentNavigationItem;
  next?: DocumentNavigationItem;
};

export type DocumentsSidebarLink = {
  canonicalUrl: string;
  label: string;
  href: string;
  status: DocumentStatus;
};

export type DocumentsSidebarParent = {
  label: string;
  items: DocumentsSidebarLink[];
};

export type DocumentsSidebarGroup = {
  label: string;
  parents: DocumentsSidebarParent[];
};
