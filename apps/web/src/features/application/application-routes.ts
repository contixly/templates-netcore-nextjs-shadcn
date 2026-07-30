import type { Route } from "next";

export const applicationRoutes = {
  home: "/" as Route,
  login: "/auth/login" as Route,
  authError: "/auth/error" as Route,
  dashboard: "/dashboard" as Route,
  welcome: "/welcome" as Route,
  workspaces: "/workspaces" as Route,
} as const;
