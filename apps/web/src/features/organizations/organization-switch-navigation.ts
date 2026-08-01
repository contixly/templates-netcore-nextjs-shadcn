import type { Route } from "next";

import { organizationRoutes } from "@/src/features/organizations/organization-routes";

const workspacePathPattern = /^\/w\/[^/]+(\/.*)?$/;
const preservableSuffixes = new Set([
  "",
  "/dashboard",
  "/settings",
  "/settings/workspace",
  "/settings/users",
  "/settings/roles",
  "/settings/teams",
  "/settings/invitations",
]);

function getPreservableSuffix(pathname?: string | null): string | undefined {
  if (!pathname) {
    return undefined;
  }

  const match = workspacePathPattern.exec(pathname);
  const suffix = match?.[1] ?? "";
  return match && preservableSuffixes.has(suffix) ? suffix : undefined;
}

export function isOrganizationSwitchPreservablePath(
  pathname?: string | null,
): boolean {
  return getPreservableSuffix(pathname) !== undefined;
}

export function resolveOrganizationSwitchHref(
  currentPathname: string | null | undefined,
  organizationKey: string,
  canManageInvitations: boolean,
): Route {
  const suffix = getPreservableSuffix(currentPathname);

  if (suffix === "/settings/invitations" && !canManageInvitations) {
    return organizationRoutes.dashboard(organizationKey);
  }

  if (suffix === undefined) {
    return organizationRoutes.dashboard(organizationKey);
  }

  const encodedKey = encodeURIComponent(organizationKey);
  return `/w/${encodedKey}${suffix}` as Route;
}
