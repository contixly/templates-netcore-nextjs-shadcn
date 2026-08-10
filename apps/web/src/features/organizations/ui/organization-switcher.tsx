"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useInsertionEffect, useLayoutEffect, useRef, useState } from "react";
import { IconCheck, IconSelector, IconSettings } from "@tabler/icons-react";

import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import { Button } from "@/src/components/ui/button";
import { useOrganizationControlInteractionReady } from "@/src/features/organizations/ui/organization-control-readiness";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/src/components/ui/dropdown-menu";
import {
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  useOptionalSidebar,
} from "@/src/components/ui/sidebar";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { resolveOrganizationSwitchHref } from "@/src/features/organizations/organization-switch-navigation";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import { setActiveBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import type { ApiFailure } from "@/src/lib/api/result";
import { cn } from "@/src/lib/utils";

export type OrganizationSwitcherItem = Readonly<{
  canManageInvitations: boolean;
  canonicalKey: string;
  id: string;
  name: string;
  slug: string;
}>;

type RouteLifetime = Readonly<{
  generation: number;
  pathname: string;
}>;

function sameRouteLifetime(
  current: RouteLifetime,
  origin: RouteLifetime,
): boolean {
  return (
    current.generation === origin.generation &&
    current.pathname === origin.pathname
  );
}

function routeKey(pathname: string): string | undefined {
  const value = /^\/w\/([^/]+)(?:\/|$)/.exec(pathname)?.[1];
  if (!value) {
    return undefined;
  }
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

export function OrganizationSwitcher({
  activeOrganizationId,
  currentOrganization,
  nextCursor,
  onNavigate,
  organizations,
}: Readonly<{
  activeOrganizationId?: string | null;
  currentOrganization?: OrganizationSwitcherItem | null;
  nextCursor?: string | null;
  onNavigate?: () => void;
  organizations: readonly OrganizationSwitcherItem[];
}>) {
  const t = useTranslations("organizations.switcher");
  const pathname = usePathname();
  const router = useRouter();
  const interactionReady = useOrganizationControlInteractionReady();
  const sidebar = useOptionalSidebar();
  const attached = useRef(true);
  const visible = useRef(true);
  const routeLifetime = useRef<RouteLifetime>({ generation: 0, pathname });
  const queuedRefresh = useRef<RouteLifetime | null>(null);
  const requestInFlight = useRef(false);
  const [open, setOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const key = routeKey(pathname);
  const currentOrganizationIsListed =
    currentOrganization !== null &&
    currentOrganization !== undefined &&
    organizations.some(
      (organization) => organization.id === currentOrganization.id,
    );
  const options = currentOrganization
    ? currentOrganizationIsListed
      ? organizations.map((organization) =>
          organization.id === currentOrganization.id
            ? currentOrganization
            : organization,
        )
      : [currentOrganization, ...organizations]
    : organizations;

  useInsertionEffect(() => {
    attached.current = true;
    return () => {
      attached.current = false;
      queuedRefresh.current = null;
    };
  }, []);

  useInsertionEffect(() => {
    if (routeLifetime.current.pathname !== pathname) {
      routeLifetime.current = {
        generation: routeLifetime.current.generation + 1,
        pathname,
      };
      queuedRefresh.current = null;
    }
  }, [pathname]);

  useLayoutEffect(() => {
    visible.current = true;
    const refreshOrigin = queuedRefresh.current;
    queuedRefresh.current = null;
    if (
      refreshOrigin &&
      attached.current &&
      sameRouteLifetime(routeLifetime.current, refreshOrigin)
    ) {
      router.refresh();
    }
    return () => {
      visible.current = false;
    };
  }, [router]);

  if (options.length === 0) {
    return null;
  }

  const activeOrganization = options.find(
    (organization) => organization.id === activeOrganizationId,
  );
  const current = key
    ? (options.find(
        (organization) =>
          organization.canonicalKey === key || organization.id === key,
      ) ??
      currentOrganization ??
      activeOrganization ??
      options[0])
    : activeOrganization;
  const currentLabel = current
    ? t("current", { name: current.name })
    : t("unselected");
  const initials = current?.name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((segment) => segment[0]?.toUpperCase() ?? "")
    .join("");
  const triggerContent = (
    <>
      <span className="sr-only">{currentLabel}</span>
      <span className="flex aspect-square size-8 shrink-0 items-center justify-center rounded-lg bg-sidebar-primary text-xs font-semibold text-sidebar-primary-foreground">
        {initials || "WS"}
      </span>
      <span className="grid min-w-0 flex-1 text-left text-sm leading-tight">
        <span className="truncate font-medium">
          {current?.name ?? t("unselected")}
        </span>
        <span className="truncate text-xs text-muted-foreground">
          {current?.slug ?? t("unselected")}
        </span>
      </span>
      <IconSelector aria-hidden="true" className="ml-auto" />
    </>
  );

  async function selectOrganization(organization: OrganizationSwitcherItem) {
    if (requestInFlight.current) {
      return;
    }
    if (
      organization.id === current?.id &&
      organization.id === activeOrganizationId
    ) {
      setOpen(false);
      return;
    }

    requestInFlight.current = true;
    setPending(true);
    setFailure(null);
    const origin = routeLifetime.current;
    const result = await setActiveBrowserOrganization(
      createBrowserApiClient(),
      { organizationId: organization.id },
    );
    if (!attached.current) {
      return;
    }
    requestInFlight.current = false;
    setPending(false);

    if (!result.ok) {
      setFailure(result.failure);
      return;
    }

    setOpen(false);
    if (!sameRouteLifetime(routeLifetime.current, origin)) {
      return;
    }
    if (!visible.current) {
      queuedRefresh.current = origin;
      return;
    }
    onNavigate?.();
    router.push(
      resolveOrganizationSwitchHref(
        origin.pathname,
        organization.canonicalKey,
        organization.canManageInvitations,
      ),
    );
    router.refresh();
  }

  return (
    <SidebarMenu>
      <SidebarMenuItem>
        <DropdownMenu
          onOpenChange={(nextOpen) => {
            if (!requestInFlight.current) {
              setOpen(nextOpen);
              if (!nextOpen) setFailure(null);
            }
          }}
          open={open}
        >
          <DropdownMenuTrigger asChild>
            {sidebar ? (
              <SidebarMenuButton
                aria-label={currentLabel}
                className="max-w-full data-[state=open]:bg-sidebar-accent data-[state=open]:text-sidebar-accent-foreground"
                data-organization-control-interaction-ready={
                  interactionReady ? "true" : undefined
                }
                disabled={!interactionReady}
                onClick={(event) => {
                  if (event.detail === 0) setOpen(true);
                }}
                size="lg"
                tooltip={currentLabel}
              >
                {triggerContent}
              </SidebarMenuButton>
            ) : (
              <Button
                aria-label={currentLabel}
                className="h-12 max-w-full justify-start gap-2 px-2 data-[state=open]:bg-sidebar-accent data-[state=open]:text-sidebar-accent-foreground"
                data-organization-control-interaction-ready={
                  interactionReady ? "true" : undefined
                }
                disabled={!interactionReady}
                onClick={(event) => {
                  if (event.detail === 0) setOpen(true);
                }}
                variant="ghost"
              >
                {triggerContent}
              </Button>
            )}
          </DropdownMenuTrigger>
          <DropdownMenuContent
            align="start"
            className="w-(--radix-dropdown-menu-trigger-width) min-w-64 rounded-lg"
            onEscapeKeyDown={(event) => pending && event.preventDefault()}
            onInteractOutside={(event) => pending && event.preventDefault()}
            side={sidebar?.isMobile ? "bottom" : "right"}
            sideOffset={4}
          >
            <DropdownMenuLabel className="text-xs text-muted-foreground">
              {t("title")}
            </DropdownMenuLabel>
            <DropdownMenuGroup>
              {options.map((organization) => {
                const organizationInitials = organization.name
                  .split(/\s+/)
                  .filter(Boolean)
                  .slice(0, 2)
                  .map((segment) => segment[0]?.toUpperCase() ?? "")
                  .join("");
                return (
                  <DropdownMenuItem
                    aria-current={
                      organization.id === current?.id ? "true" : undefined
                    }
                    aria-label={t("switchTo", { name: organization.name })}
                    className="gap-2 p-2"
                    disabled={pending}
                    key={organization.id}
                    onSelect={() => void selectOrganization(organization)}
                  >
                    <span className="flex size-6 items-center justify-center rounded-md border text-[11px] font-semibold">
                      {organizationInitials || "WS"}
                    </span>
                    <span className="grid min-w-0 flex-1 text-left text-sm leading-tight">
                      <span className="truncate">{organization.name}</span>
                      <span className="truncate text-xs text-muted-foreground">
                        {organization.slug}
                      </span>
                    </span>
                    <IconCheck
                      aria-hidden="true"
                      className={cn(
                        organization.id === current?.id
                          ? "opacity-100"
                          : "opacity-0",
                      )}
                    />
                  </DropdownMenuItem>
                );
              })}
            </DropdownMenuGroup>
            {failure ? (
              <DropdownMenuLabel>
                <Alert>
                  <AlertTitle>{t("failure")}</AlertTitle>
                  {failure.kind === "problem" && failure.traceId ? (
                    <AlertDescription className="font-mono text-xs">
                      {failure.traceId}
                    </AlertDescription>
                  ) : null}
                </Alert>
              </DropdownMenuLabel>
            ) : null}
            <DropdownMenuSeparator />
            <DropdownMenuGroup>
              <DropdownMenuItem asChild className="gap-2 p-2">
                <Link
                  aria-label={t("manage")}
                  href={organizationRoutes.workspaces}
                  onClick={() => {
                    setOpen(false);
                    onNavigate?.();
                  }}
                  onNavigate={() => setOpen(false)}
                >
                  <span className="flex size-6 items-center justify-center rounded-md border">
                    <IconSettings aria-hidden="true" />
                  </span>
                  <span>{t("manage")}</span>
                </Link>
              </DropdownMenuItem>
              {nextCursor ? (
                <DropdownMenuItem asChild className="gap-2 p-2">
                  <Link
                    aria-label={t("loadMore")}
                    href={organizationRoutes.workspaces}
                    onClick={() => {
                      setOpen(false);
                      onNavigate?.();
                    }}
                    onNavigate={() => setOpen(false)}
                  >
                    <span className="flex size-6 items-center justify-center rounded-md border">
                      <IconSettings aria-hidden="true" />
                    </span>
                    <span>{t("loadMore")}</span>
                  </Link>
                </DropdownMenuItem>
              ) : null}
            </DropdownMenuGroup>
          </DropdownMenuContent>
        </DropdownMenu>
      </SidebarMenuItem>
    </SidebarMenu>
  );
}
