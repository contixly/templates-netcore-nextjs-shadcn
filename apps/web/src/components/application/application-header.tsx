"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { IconBooks } from "@tabler/icons-react";

import { ApplicationBreadcrumbs } from "@/src/components/application/application-breadcrumbs";
import { ThemeSwitcher } from "@/src/components/application/theme-switcher";
import { Button } from "@/src/components/ui/button";
import { Separator } from "@/src/components/ui/separator";
import { SidebarTrigger, useSidebar } from "@/src/components/ui/sidebar";
import { applicationRoutes } from "@/src/features/application/application-routes";

export function ApplicationHeader() {
  const t = useTranslations("application.shell");
  const { isMobile, openMobile, state } = useSidebar();
  const sidebarOpen = isMobile ? openMobile : state === "expanded";
  const sidebarAction = t(sidebarOpen ? "sidebar.close" : "sidebar.open");

  return (
    <header
      className="sticky top-0 flex h-(--header-height) shrink-0 items-center gap-2 border-b bg-background px-4"
      data-slot="application-header"
    >
      <SidebarTrigger aria-label={sidebarAction} title={sidebarAction} />
      <Separator className="h-4" orientation="vertical" />
      <div className="min-w-0 flex-1">
        <ApplicationBreadcrumbs />
      </div>
      <Button asChild size="icon-sm" variant="ghost">
        <Link
          aria-label={t("navigation.documentation")}
          href={applicationRoutes.docs}
          title={t("navigation.documentation")}
        >
          <IconBooks aria-hidden="true" />
        </Link>
      </Button>
      <ThemeSwitcher />
    </header>
  );
}
