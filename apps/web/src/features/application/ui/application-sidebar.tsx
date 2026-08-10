"use client";

import { useTranslations } from "next-intl";
import { IconX } from "@tabler/icons-react";

import { AccountNavigation } from "@/src/features/application/ui/account-navigation";
import { PrimaryNavigation } from "@/src/features/application/ui/primary-navigation";
import { OrganizationSwitcher } from "@/src/features/organizations/ui/organization-switcher";
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
import type { CSSProperties } from "react";

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
  const dashboardHref = data.currentOrganization
    ? organizationRoutes.dashboard(data.currentOrganization.canonicalKey)
    : applicationRoutes.dashboard;

  return (
    <Sidebar
      collapsible="icon"
      mobileDescription={t("mobileDescription")}
      mobileTitle={t("mobileTitle")}
    >
      <SidebarHeader className="flex-row items-start">
        <div className="min-w-0 flex-1">
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
        <div className="shrink-0">
          <MobileSidebarClose label={t("close")} />
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
      <SidebarProvider
        style={
          {
            "--header-height": "calc(var(--spacing) * 12)",
            "--sidebar-width": "calc(var(--spacing) * 72)",
          } as CSSProperties
        }
      >
        {content}
      </SidebarProvider>
    </TooltipProvider>
  );
}
