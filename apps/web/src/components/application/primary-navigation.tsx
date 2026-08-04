"use client";

import Link from "next/link";
import type { Route } from "next";
import { useTranslations } from "next-intl";
import {
  IconBooks,
  IconBuildingCommunity,
  IconLayoutDashboard,
} from "@tabler/icons-react";

import { OrganizationCreateDialog } from "@/src/components/organizations/organization-create-dialog";
import {
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/src/components/ui/sidebar";
import { applicationRoutes } from "@/src/features/application/application-routes";
import { useMobileSidebarClose } from "@/src/hooks/use-mobile-sidebar-close";

export function PrimaryNavigation({
  dashboardHref,
  pathname,
}: Readonly<{
  dashboardHref: string;
  pathname: string;
}>) {
  const t = useTranslations("application.shell.navigation");
  const closeMobileSidebar = useMobileSidebarClose();
  const items = [
    {
      href: dashboardHref,
      icon: IconLayoutDashboard,
      label: t("dashboard"),
      active: pathname === dashboardHref,
      current: pathname === dashboardHref,
    },
    {
      href: applicationRoutes.workspaces,
      icon: IconBuildingCommunity,
      label: t("workspaces"),
      active:
        pathname === applicationRoutes.workspaces ||
        pathname === applicationRoutes.welcome,
      current: pathname === applicationRoutes.workspaces,
    },
    {
      href: applicationRoutes.docs,
      icon: IconBooks,
      label: t("documentation"),
      active: pathname === applicationRoutes.docs,
      current: pathname === applicationRoutes.docs,
    },
  ] as const;

  return (
    <div className="flex flex-col gap-2">
      <SidebarMenu>
        {items.map(({ active, current, href, icon: Icon, label }) => (
          <SidebarMenuItem key={href}>
            <SidebarMenuButton asChild isActive={active} tooltip={label}>
              <Link
                aria-current={current ? "page" : undefined}
                href={href as Route}
                onClick={closeMobileSidebar}
              >
                <Icon aria-hidden="true" />
                <span>{label}</span>
              </Link>
            </SidebarMenuButton>
          </SidebarMenuItem>
        ))}
      </SidebarMenu>
      <div className="group-data-[collapsible=icon]:hidden">
        <OrganizationCreateDialog onNavigate={closeMobileSidebar} />
      </div>
    </div>
  );
}
