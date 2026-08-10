"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import {
  IconBuilding,
  IconKey,
  IconMail,
  IconMenu2,
  IconUsers,
  IconUsersGroup,
  IconUserShield,
} from "@tabler/icons-react";

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
import { organizationRoutes } from "@/src/features/organizations/organization-routes";

type NavigationProps = Readonly<{
  canManageApiKeys: boolean;
  canManageInvitations: boolean;
  organizationKey: string;
  pathname: string;
}>;

function OrganizationSettingsMenu({
  canManageApiKeys,
  canManageInvitations,
  onNavigate,
  organizationKey,
  pathname,
}: NavigationProps & Readonly<{ onNavigate?: () => void }>) {
  const t = useTranslations("organizations.settings.navigation");
  const items = [
    {
      href: organizationRoutes.settingsWorkspace(organizationKey),
      icon: IconBuilding,
      label: "workspace",
    },
    ...(canManageInvitations
      ? [
          {
            href: organizationRoutes.settingsInvitations(organizationKey),
            icon: IconMail,
            label: "invitations" as const,
          },
        ]
      : []),
    {
      href: organizationRoutes.settingsUsers(organizationKey),
      icon: IconUsers,
      label: "users",
    },
    {
      href: organizationRoutes.settingsTeams(organizationKey),
      icon: IconUsersGroup,
      label: "teams",
    },
    {
      href: organizationRoutes.settingsRoles(organizationKey),
      icon: IconUserShield,
      label: "roles",
    },
    ...(canManageApiKeys
      ? [
          {
            href: organizationRoutes.settingsApiKeys(organizationKey),
            icon: IconKey,
            label: "apiKeys" as const,
          },
        ]
      : []),
  ] as const;

  return (
    <SidebarMenu>
      {items.map((item) => {
        const active =
          pathname === item.href || pathname.startsWith(`${item.href}/`);
        const Icon = item.icon;

        return (
          <SidebarMenuItem key={item.href}>
            <SidebarMenuButton asChild isActive={active}>
              <Link
                aria-current={active ? "page" : undefined}
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

function OrganizationSettingsNavContent(props: NavigationProps) {
  const t = useTranslations("organizations.settings.navigation");
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
              aria-label={t("label")}
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
              <DrawerDescription>{t("loading")}</DrawerDescription>
            </DrawerHeader>
            <nav aria-label={t("label")} className="px-2 pb-4">
              <OrganizationSettingsMenu
                {...props}
                onNavigate={() => setMobileOpen(false)}
              />
            </nav>
          </DrawerContent>
        </Drawer>
      </div>

      <nav
        aria-label={t("label")}
        className="hidden w-full shrink-0 border-r p-2 md:block md:w-64"
        data-slot="organization-settings-nav"
      >
        <OrganizationSettingsMenu {...props} />
      </nav>
    </>
  );
}

function OrganizationSettingsNavWithProvider(props: NavigationProps) {
  const sidebar = useOptionalSidebar();
  const content = <OrganizationSettingsNavContent {...props} />;

  return sidebar ? (
    content
  ) : (
    <SidebarProvider className="block min-h-0 w-full bg-transparent">
      {content}
    </SidebarProvider>
  );
}

function CurrentOrganizationSettingsNav(
  props: Omit<NavigationProps, "pathname">,
) {
  return (
    <OrganizationSettingsNavWithProvider {...props} pathname={usePathname()} />
  );
}

export function OrganizationSettingsNav({
  canManageApiKeys,
  canManageInvitations,
  organizationKey,
  pathname,
}: Readonly<{
  canManageApiKeys: boolean;
  canManageInvitations: boolean;
  organizationKey: string;
  pathname?: string;
}>) {
  const props = {
    canManageApiKeys,
    canManageInvitations,
    organizationKey,
  };

  return pathname === undefined ? (
    <CurrentOrganizationSettingsNav {...props} />
  ) : (
    <OrganizationSettingsNavWithProvider {...props} pathname={pathname} />
  );
}
