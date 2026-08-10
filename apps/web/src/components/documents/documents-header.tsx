"use client";

import { IconHome, IconMenu2 } from "@tabler/icons-react";
import Link from "next/link";
import { useTranslations } from "next-intl";

import { ThemeSwitcher } from "@/src/features/application/ui/theme-switcher";
import { DocumentsSearch } from "@/src/components/documents/documents-search";
import { Button } from "@/src/components/ui/button";
import { applicationRoutes } from "@/src/features/application/application-routes";
import { documentsRoutes } from "@/src/features/documents/documents-routes";

export function DocumentsHeader({
  onOpenNavigation,
}: Readonly<{ onOpenNavigation: () => void }>) {
  const t = useTranslations("documents");

  return (
    <header className="sticky top-0 border-b bg-background">
      <div className="mx-auto flex h-14 max-w-7xl min-w-0 items-center gap-2 px-4 sm:gap-4">
        <Button
          aria-label={t("sidebar.open")}
          className="lg:hidden"
          onClick={onOpenNavigation}
          size="icon"
          variant="outline"
        >
          <IconMenu2 aria-hidden="true" />
        </Button>
        <Link
          className="shrink-0 font-semibold tracking-tight"
          href={documentsRoutes.root}
        >
          {t("navigation.label")}
        </Link>
        <div className="ml-auto flex min-w-0 items-center gap-2">
          <DocumentsSearch />
          <Button asChild size="icon" variant="outline">
            <Link
              aria-label={t("navigation.home")}
              href={applicationRoutes.home}
              title={t("navigation.home")}
            >
              <IconHome aria-hidden="true" />
            </Link>
          </Button>
          <ThemeSwitcher />
        </div>
      </div>
    </header>
  );
}
