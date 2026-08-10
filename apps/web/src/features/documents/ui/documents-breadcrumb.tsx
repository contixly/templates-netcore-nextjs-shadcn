import Link from "next/link";
import { useTranslations } from "next-intl";

import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/src/components/ui/breadcrumb";
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
    <Breadcrumb
      aria-label={
        current
          ? `${t("navigation.label")}: ${current.title}`
          : t("navigation.label")
      }
      className="min-w-0"
    >
      <BreadcrumbList className="min-w-0 flex-nowrap overflow-hidden">
        <BreadcrumbItem>
          <BreadcrumbLink asChild>
            <Link href="/docs">{t("navigation.label")}</Link>
          </BreadcrumbLink>
        </BreadcrumbItem>
        {current ? (
          <>
            <BreadcrumbSeparator className="hidden lg:block" />
            <BreadcrumbItem className="hidden lg:block">
              <BreadcrumbPage>{current.group}</BreadcrumbPage>
            </BreadcrumbItem>
            <BreadcrumbSeparator className="hidden xl:block" />
            <BreadcrumbItem className="hidden xl:block">
              <BreadcrumbPage>{current.parent}</BreadcrumbPage>
            </BreadcrumbItem>
            <BreadcrumbSeparator />
            <BreadcrumbItem className="min-w-0">
              <BreadcrumbPage className="min-w-0 truncate">
                {current.title}
              </BreadcrumbPage>
            </BreadcrumbItem>
          </>
        ) : null}
      </BreadcrumbList>
    </Breadcrumb>
  );
}
