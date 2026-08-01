import type { Route } from "next";

function organizationPath(organizationKey: string, suffix = ""): Route {
  return `/w/${encodeURIComponent(organizationKey)}${suffix}` as Route;
}

export const organizationRoutes = {
  welcome: "/welcome" as Route,
  workspaces: "/workspaces" as Route,
  workspace: (organizationKey: string) => organizationPath(organizationKey),
  dashboard: (organizationKey: string) =>
    organizationPath(organizationKey, "/dashboard"),
  settings: (organizationKey: string) =>
    organizationPath(organizationKey, "/settings"),
  settingsWorkspace: (organizationKey: string) =>
    organizationPath(organizationKey, "/settings/workspace"),
  settingsUsers: (organizationKey: string) =>
    organizationPath(organizationKey, "/settings/users"),
  settingsRoles: (organizationKey: string) =>
    organizationPath(organizationKey, "/settings/roles"),
} as const;
