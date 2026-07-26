import type { Route } from "next";

export const applicationRoutes = {
  home: "/" as Route,
  login: "/auth/login" as Route,
  dashboard: "/dashboard" as Route,
} as const;
