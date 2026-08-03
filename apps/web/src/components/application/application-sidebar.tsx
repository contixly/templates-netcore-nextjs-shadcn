"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { IconX } from "@tabler/icons-react";

import { AccountNavigation } from "@/src/components/application/account-navigation";
import { PrimaryNavigation } from "@/src/components/application/primary-navigation";
import { OrganizationSwitcher } from "@/src/components/organizations/organization-switcher";
import { Button } from "@/src/components/ui/button";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarProvider,
  SidebarRail,
  useOptionalSidebar,
  useSidebar,
} from "@/src/components/ui/sidebar";
import { TooltipProvider } from "@/src/components/ui/tooltip";
import { applicationRoutes } from "@/src/features/application/application-routes";
import type { ApplicationShellData } from "@/src/features/application/application-shell-model";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { useMobileSidebarClose } from "@/src/hooks/use-mobile-sidebar-close";

function MobileSidebarClose({ label }: Readonly<{ label: string }>) {
  const { isMobile, setOpenMobile } = useSidebar();
  if (!isMobile) {
    return null;
  }

  return (
    <Button
      aria-label={label}
      onClick={() => setOpenMobile(false)}
      size="icon-sm"
      title={label}
      type="button"
      variant="ghost"
    >
      <IconX aria-hidden="true" />
    </Button>
  );
}

function ApplicationSidebarRail({
  closeLabel,
  openLabel,
}: Readonly<{ closeLabel: string; openLabel: string }>) {
  const { isMobile, state } = useSidebar();
  if (isMobile) {
    return null;
  }

  const label = state === "expanded" ? closeLabel : openLabel;
  return <SidebarRail aria-label={label} title={label} />;
}

function ApplicationSidebarContent({
  data,
  pathname,
}: Readonly<{
  data: ApplicationShellData;
  pathname: string;
}>) {
  const t = useTranslations("application.shell.sidebar");
  const closeMobileSidebar = useMobileSidebarClose();
  const { isMobile, state } = useSidebar();
  const dashboardHref = data.currentOrganization
    ? organizationRoutes.dashboard(data.currentOrganization.canonicalKey)
    : applicationRoutes.dashboard;

  return (
    <Sidebar
      collapsible="icon"
      mobileDescription={t("mobileDescription")}
      mobileTitle={t("mobileTitle")}
    >
      <SidebarHeader>
        <div className="flex items-center gap-2">
          <Link
            aria-label={
              !isMobile && state === "collapsed"
                ? t("brandHomeLabel")
                : undefined
            }
            className="flex h-8 min-w-0 flex-1 items-center gap-2 px-2 text-sm font-semibold group-data-[collapsible=icon]:justify-center group-data-[collapsible=icon]:px-0"
            href={dashboardHref}
            onClick={closeMobileSidebar}
          >
            <span aria-hidden="true">T</span>
            <span className="group-data-[collapsible=icon]:hidden">
              Template
            </span>
          </Link>
          <MobileSidebarClose label={t("close")} />
        </div>
        <div className="group-data-[collapsible=icon]:hidden">
          <OrganizationSwitcher
            activeOrganizationId={data.session.activeOrganizationId}
            currentOrganization={
              data.currentOrganization
                ? {
                    ...data.currentOrganization,
                    canManageInvitations:
                      data.currentOrganization.capabilities
                        .canManageInvitations,
                  }
                : null
            }
            nextCursor={data.nextOrganizationCursor}
            onNavigate={closeMobileSidebar}
            organizations={data.organizations.map((organization) => ({
              ...organization,
              canManageInvitations:
                organization.capabilities.canManageInvitations,
            }))}
          />
        </div>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>{t("workspace")}</SidebarGroupLabel>
          <SidebarGroupContent>
            <nav aria-label={t("workspace")} data-slot="application-navigation">
              <PrimaryNavigation
                dashboardHref={dashboardHref}
                pathname={pathname}
              />
            </nav>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>
      <SidebarFooter>
        <AccountNavigation account={data.account} pathname={pathname} />
      </SidebarFooter>
      <ApplicationSidebarRail closeLabel={t("close")} openLabel={t("open")} />
    </Sidebar>
  );
}

export function ApplicationSidebar({
  data,
  pathname,
}: Readonly<{
  data: ApplicationShellData;
  pathname: string;
}>) {
  const sidebar = useOptionalSidebar();
  const content = <ApplicationSidebarContent data={data} pathname={pathname} />;

  return sidebar ? (
    content
  ) : (
    <TooltipProvider>
      <SidebarProvider>{content}</SidebarProvider>
    </TooltipProvider>
  );
}
