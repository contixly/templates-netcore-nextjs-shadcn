"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { IconBooks } from "@tabler/icons-react";

import { ApplicationBreadcrumbs } from "@/src/features/application/ui/application-breadcrumbs";
import { ThemeSwitcher } from "@/src/features/application/ui/theme-switcher";
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
      className="sticky top-0 z-20 flex h-(--header-height) shrink-0 items-center border-b bg-background transition-[width,height] ease-linear group-has-data-[collapsible=icon]/sidebar-wrapper:h-(--header-height)"
      data-slot="application-header"
    >
      <div className="flex w-full min-w-0 items-center gap-1 pr-2 pl-1 md:pl-4 lg:gap-2 lg:pl-4">
        <SidebarTrigger
          aria-label={sidebarAction}
          className="md:-ml-1"
          size="icon"
          title={sidebarAction}
          variant="outline"
        />
        <Separator className="mx-1 h-4" orientation="vertical" />
        <div className="min-w-0 flex-1">
          <ApplicationBreadcrumbs />
        </div>
        <div className="ml-auto flex shrink-0 items-center gap-2">
          <Button asChild size="icon" variant="outline">
            <Link
              aria-label={t("navigation.documentation")}
              href={applicationRoutes.docs}
              title={t("navigation.documentation")}
            >
              <IconBooks aria-hidden="true" />
            </Link>
          </Button>
          <ThemeSwitcher />
        </div>
      </div>
    </header>
  );
}
