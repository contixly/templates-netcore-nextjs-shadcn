"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useInsertionEffect, useLayoutEffect, useRef, useState } from "react";
import { IconCheck, IconSelector } from "@tabler/icons-react";

import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import { useOrganizationControlInteractionReady } from "@/src/components/organizations/organization-control-readiness";
import { Button } from "@/src/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/src/components/ui/dialog";
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
    <Dialog
      open={open}
      onOpenChange={(nextOpen) => {
        if (!requestInFlight.current) {
          setOpen(nextOpen);
          if (!nextOpen) {
            setFailure(null);
          }
        }
      }}
    >
      <DialogTrigger asChild>
        <Button
          className="max-w-full min-w-0"
          data-organization-control-interaction-ready={
            interactionReady ? "true" : undefined
          }
          disabled={!interactionReady}
          type="button"
          variant="outline"
        >
          <IconSelector data-icon="inline-start" />
          <span className="max-w-40 min-w-0 truncate">
            {current ? t("current", { name: current.name }) : t("unselected")}
          </span>
        </Button>
      </DialogTrigger>
      <DialogContent
        onEscapeKeyDown={(event) => {
          if (pending) {
            event.preventDefault();
          }
        }}
        onInteractOutside={(event) => {
          if (pending) {
            event.preventDefault();
          }
        }}
      >
        <DialogHeader>
          <DialogTitle>{t("title")}</DialogTitle>
          <DialogDescription>{t("description")}</DialogDescription>
        </DialogHeader>
        <div
          className="flex max-h-72 flex-col gap-1 overflow-y-auto"
          role="list"
        >
          {options.map((organization) => (
            <div key={organization.id} role="listitem">
              <Button
                aria-current={
                  organization.id === current?.id ? "true" : undefined
                }
                aria-label={t("switchTo", { name: organization.name })}
                className="w-full min-w-0 justify-start"
                disabled={pending}
                onClick={() => selectOrganization(organization)}
                type="button"
                variant="ghost"
              >
                <IconCheck
                  className={cn(
                    "shrink-0",
                    organization.id === current?.id
                      ? "opacity-100"
                      : "opacity-0",
                  )}
                  data-icon="inline-start"
                />
                <span className="min-w-0 flex-1 truncate text-left">
                  {organization.name}
                </span>
              </Button>
            </div>
          ))}
        </div>
        {failure ? (
          <Alert>
            <AlertTitle>{t("failure")}</AlertTitle>
            {failure.kind === "problem" && failure.traceId ? (
              <AlertDescription className="font-mono text-xs">
                {failure.traceId}
              </AlertDescription>
            ) : null}
          </Alert>
        ) : null}
        <div className="flex flex-col gap-2 sm:flex-row">
          <Button asChild className="sm:flex-1" variant="outline">
            <Link
              href={organizationRoutes.workspaces}
              onClick={() => {
                setOpen(false);
                onNavigate?.();
              }}
              onNavigate={() => setOpen(false)}
            >
              {t("manage")}
            </Link>
          </Button>
          {nextCursor ? (
            <Button asChild className="sm:flex-1" variant="outline">
              <Link
                href={organizationRoutes.workspaces}
                onClick={() => {
                  setOpen(false);
                  onNavigate?.();
                }}
                onNavigate={() => setOpen(false)}
              >
                {t("loadMore")}
              </Link>
            </Button>
          ) : null}
        </div>
      </DialogContent>
    </Dialog>
  );
}
