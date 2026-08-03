import Link from "next/link";
import { useTranslations } from "next-intl";

import type { DocumentsSidebarGroup } from "@/src/features/documents/documents-types";

export type DocumentBreadcrumbContext = {
  group: string;
  parent: string;
  title: string;
};

export function findDocumentBreadcrumbContext(
  navigation: DocumentsSidebarGroup[],
  href: string,
): DocumentBreadcrumbContext | undefined {
  for (const group of navigation) {
    for (const parent of group.parents) {
      const item = parent.items.find((candidate) => candidate.href === href);
      if (item) {
        return { group: group.label, parent: parent.label, title: item.label };
      }
    }
  }
}

export function DocumentsBreadcrumb({
  current,
}: Readonly<{ current?: DocumentBreadcrumbContext }>) {
  const t = useTranslations("documents");

  return (
    <nav
      aria-label={
        current
          ? `${t("navigation.label")}: ${current.title}`
          : t("navigation.label")
      }
      className="min-w-0 text-xs text-muted-foreground"
    >
      <ol className="flex min-w-0 flex-wrap items-center gap-2">
        <li>
          <Link className="hover:text-foreground" href="/docs">
            {t("navigation.label")}
          </Link>
        </li>
        {current ? (
          <>
            <li aria-hidden="true">/</li>
            <li>{current.group}</li>
            <li aria-hidden="true">/</li>
            <li>{current.parent}</li>
            <li aria-hidden="true">/</li>
            <li
              aria-current="page"
              className="min-w-0 truncate text-foreground"
            >
              {current.title}
            </li>
          </>
        ) : null}
      </ol>
    </nav>
  );
}
