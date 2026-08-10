"use client";

import { IconHome, IconLayoutSidebar } from "@tabler/icons-react";
import Link from "next/link";
import { useTranslations } from "next-intl";

import {
  type DocumentBreadcrumbContext,
  DocumentsBreadcrumb,
} from "@/src/features/documents/ui/documents-breadcrumb";
import { ThemeSwitcher } from "@/src/features/application/ui/theme-switcher";
import { DocumentsSearch } from "@/src/features/documents/ui/documents-search";
import { Button, buttonVariants } from "@/src/components/ui/button";
import { useOptionalSidebar } from "@/src/components/ui/sidebar";
import { applicationRoutes } from "@/src/features/application/application-routes";

export function DocumentsHeader({
  current,
  onOpenNavigation,
}: Readonly<{
  current?: DocumentBreadcrumbContext;
  onOpenNavigation: () => void;
}>) {
  const t = useTranslations("documents");
  const sidebar = useOptionalSidebar();

  return (
    <header className="flex h-16 shrink-0 items-center gap-2 border-b transition-[width,height] ease-linear group-has-data-[collapsible=offcanvas]/sidebar-wrapper:h-12">
      <div className="flex min-w-0 flex-1 items-center gap-2 px-4">
        <Button
          aria-label={t("sidebar.open")}
          className="-ml-1"
          onClick={() => {
            onOpenNavigation();
            sidebar?.toggleSidebar();
          }}
          size="icon-sm"
          variant="ghost"
        >
          <IconLayoutSidebar aria-hidden="true" />
        </Button>
        <DocumentsBreadcrumb current={current} />
      </div>
      <div className="shrink-0 px-3">
        <div className="flex items-center gap-2 text-sm">
          <DocumentsSearch />
          <Link
            aria-label={t("navigation.home")}
            className={buttonVariants({ variant: "outline", size: "icon" })}
            href={applicationRoutes.home}
            title={t("navigation.home")}
          >
            <IconHome aria-hidden="true" />
          </Link>
          <ThemeSwitcher />
        </div>
      </div>
    </header>
  );
}
