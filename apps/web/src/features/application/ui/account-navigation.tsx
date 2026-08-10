"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { IconUser } from "@tabler/icons-react";

import { LogoutButton } from "@/src/features/authentication/ui/logout-button";
import {
  Avatar,
  AvatarFallback,
  AvatarImage,
} from "@/src/components/ui/avatar";
import {
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/src/components/ui/sidebar";
import { accountRoutes } from "@/src/features/account/account-routes";
import type { ApplicationShellData } from "@/src/features/application/application-shell-model";
import { useMobileSidebarClose } from "@/src/hooks/use-mobile-sidebar-close";

function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  return parts
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("");
}

export function AccountNavigation({
  account,
  pathname,
}: Readonly<{
  account: ApplicationShellData["account"];
  pathname: string;
}>) {
  const t = useTranslations("application.shell.navigation");
  const closeMobileSidebar = useMobileSidebarClose();
  const active =
    pathname === accountRoutes.root ||
    pathname.startsWith(`${accountRoutes.root}/`);
  const current = pathname === accountRoutes.profile;

  return (
    <div className="flex flex-col gap-2">
      <SidebarMenu>
        <SidebarMenuItem>
          <SidebarMenuButton
            asChild
            isActive={active}
            size="lg"
            tooltip={t("account")}
          >
            <Link
              aria-current={current ? "page" : undefined}
              href={accountRoutes.profile}
              onClick={closeMobileSidebar}
            >
              <Avatar size="sm">
                {account.imageUrl ? (
                  <AvatarImage alt="" src={account.imageUrl} />
                ) : null}
                <AvatarFallback>
                  {initials(account.displayName) || (
                    <IconUser aria-hidden="true" />
                  )}
                </AvatarFallback>
              </Avatar>
              <span className="min-w-0 truncate">{account.displayName}</span>
            </Link>
          </SidebarMenuButton>
        </SidebarMenuItem>
      </SidebarMenu>
      <div className="group-data-[collapsible=icon]:hidden">
        <LogoutButton />
      </div>
    </div>
  );
}
