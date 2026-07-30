"use client";

import Link from "next/link";
import type { Route } from "next";
import { usePathname, useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useRef, useState } from "react";
import { IconCheck, IconSelector } from "@tabler/icons-react";

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
import type { OrganizationSummaryResponse } from "@/src/lib/api/generated/types.gen";
import { setActiveBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import type { ApiFailure } from "@/src/lib/api/result";

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
  nextCursor,
  organizations,
}: Readonly<{
  activeOrganizationId?: string | null;
  nextCursor?: string | null;
  organizations: readonly OrganizationSummaryResponse[];
}>) {
  const t = useTranslations("organizations.switcher");
  const pathname = usePathname();
  const router = useRouter();
  const requestInFlight = useRef(false);
  const [open, setOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const key = routeKey(pathname);

  if (!key || organizations.length === 0) {
    return null;
  }

  const current =
    organizations.find(
      (organization) =>
        organization.canonicalKey === key || organization.id === key,
    ) ??
    organizations.find(
      (organization) => organization.id === activeOrganizationId,
    ) ??
    organizations[0];

  async function selectOrganization(organization: OrganizationSummaryResponse) {
    if (requestInFlight.current || organization.id === current.id) {
      setOpen(false);
      return;
    }

    requestInFlight.current = true;
    setPending(true);
    setFailure(null);
    const result = await setActiveBrowserOrganization(
      createBrowserApiClient(),
      { organizationId: organization.id },
    );
    requestInFlight.current = false;
    setPending(false);

    if (!result.ok) {
      setFailure(result.failure);
      return;
    }

    setOpen(false);
    router.push(
      resolveOrganizationSwitchHref(pathname, organization.canonicalKey),
    );
    router.refresh();
  }

  const moreHref = nextCursor
    ? (`${organizationRoutes.workspaces}?cursor=${encodeURIComponent(nextCursor)}` as Route)
    : organizationRoutes.workspaces;

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
        <Button type="button" variant="outline">
          <IconSelector data-icon="inline-start" />
          <span className="max-w-40 truncate">
            {t("current", { name: current.name })}
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
          {organizations.map((organization) => (
            <div key={organization.id} role="listitem">
              <Button
                aria-label={t("switchTo", { name: organization.name })}
                className="w-full justify-start"
                disabled={pending}
                onClick={() => selectOrganization(organization)}
                type="button"
                variant="ghost"
              >
                <IconCheck
                  className={
                    organization.id === current.id ? "opacity-100" : "opacity-0"
                  }
                  data-icon="inline-start"
                />
                <span className="truncate">{organization.name}</span>
              </Button>
            </div>
          ))}
        </div>
        {failure ? (
          <div className="flex flex-col gap-1" role="alert">
            <p>{t("failure")}</p>
            {failure.kind === "problem" && failure.traceId ? (
              <p className="font-mono text-xs text-muted-foreground">
                {failure.traceId}
              </p>
            ) : null}
          </div>
        ) : null}
        <div className="flex flex-col gap-2 sm:flex-row">
          <Button asChild className="sm:flex-1" variant="outline">
            <Link href={organizationRoutes.workspaces}>{t("manage")}</Link>
          </Button>
          {nextCursor ? (
            <Button asChild className="sm:flex-1" variant="outline">
              <Link href={moreHref}>{t("loadMore")}</Link>
            </Button>
          ) : null}
        </div>
      </DialogContent>
    </Dialog>
  );
}
