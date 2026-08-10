"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import {
  IconAlertTriangle,
  IconKey,
  IconLink,
  IconMail,
  IconMenu2,
  IconShieldLock,
  IconUser,
} from "@tabler/icons-react";
import { useState } from "react";

import { Button } from "@/src/components/ui/button";
import {
  Drawer,
  DrawerContent,
  DrawerDescription,
  DrawerHeader,
  DrawerTitle,
  DrawerTrigger,
} from "@/src/components/ui/drawer";
import {
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  useOptionalSidebar,
} from "@/src/components/ui/sidebar";
import { accountRoutes } from "@/src/features/account/account-routes";
import { cn } from "@/src/lib/utils";

const accountNavigation = [
  {
    href: accountRoutes.profile,
    icon: IconUser,
    label: "profile",
    destructive: false,
  },
  {
    href: accountRoutes.invitations,
    icon: IconMail,
    label: "invitations",
    destructive: false,
  },
  {
    href: accountRoutes.connections,
    icon: IconLink,
    label: "connections",
    destructive: false,
  },
  {
    href: accountRoutes.security,
    icon: IconShieldLock,
    label: "security",
    destructive: false,
  },
  {
    href: accountRoutes.apiKeys,
    icon: IconKey,
    label: "apiKeys",
    destructive: false,
  },
  {
    href: accountRoutes.danger,
    icon: IconAlertTriangle,
    label: "danger",
    destructive: true,
  },
] as const;

function isActiveAccountRoute(pathname: string, href: string): boolean {
  return pathname === href || pathname.startsWith(`${href}/`);
}

export function AccountNav({
  pathname: pathnameOverride,
}: Readonly<{ pathname?: string }>) {
  if (pathnameOverride !== undefined) {
    return <AccountNavLinks pathname={pathnameOverride} />;
  }

  return <CurrentAccountNav />;
}

function CurrentAccountNav() {
  return <AccountNavLinks pathname={usePathname()} />;
}

function AccountMenu({
  onNavigate,
  pathname,
}: Readonly<{ onNavigate?: () => void; pathname: string }>) {
  const t = useTranslations("account.navigation");

  return (
    <SidebarMenu>
      {accountNavigation.map((item) => {
        const active = isActiveAccountRoute(pathname, item.href);
        const Icon = item.icon;

        return (
          <SidebarMenuItem key={item.href}>
            <SidebarMenuButton asChild isActive={active}>
              <Link
                aria-current={active ? "page" : undefined}
                className={cn(
                  "text-muted-foreground",
                  item.destructive && !active && "text-destructive",
                )}
                href={item.href}
                onClick={onNavigate}
              >
                <Icon aria-hidden="true" />
                <span>{t(item.label)}</span>
              </Link>
            </SidebarMenuButton>
          </SidebarMenuItem>
        );
      })}
    </SidebarMenu>
  );
}

function AccountNavLinks({ pathname }: Readonly<{ pathname: string }>) {
  const sidebar = useOptionalSidebar();

  if (sidebar) {
    return <AccountNavContent pathname={pathname} />;
  }

  return (
    <SidebarProvider className="block min-h-0 w-full bg-transparent">
      <AccountNavContent pathname={pathname} />
    </SidebarProvider>
  );
}

function AccountNavContent({ pathname }: Readonly<{ pathname: string }>) {
  const t = useTranslations("account.navigation");
  const [mobileOpen, setMobileOpen] = useState(false);

  return (
    <>
      <div className="border-b p-2 md:hidden">
        <Drawer
          direction="right"
          onOpenChange={setMobileOpen}
          open={mobileOpen}
        >
          <DrawerTrigger asChild>
            <Button
              aria-label={t("open")}
              className="w-full justify-start"
              type="button"
              variant="outline"
            >
              <IconMenu2 aria-hidden="true" data-icon="inline-start" />
              {t("label")}
            </Button>
          </DrawerTrigger>
          <DrawerContent>
            <DrawerHeader>
              <DrawerTitle>{t("label")}</DrawerTitle>
              <DrawerDescription>{t("description")}</DrawerDescription>
            </DrawerHeader>
            <nav aria-label={t("label")} className="px-2 pb-4">
              <AccountMenu
                onNavigate={() => setMobileOpen(false)}
                pathname={pathname}
              />
            </nav>
          </DrawerContent>
        </Drawer>
      </div>

      <nav
        aria-label={t("label")}
        className="hidden w-full border-r p-2 md:block"
        data-slot="account-settings-nav"
      >
        <AccountMenu pathname={pathname} />
      </nav>
    </>
  );
}
