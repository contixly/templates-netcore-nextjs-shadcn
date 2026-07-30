import type { Route } from "next";

export const authenticationRoutes = {
  login: "/auth/login" as Route,
  error: "/auth/error" as Route,
  dashboard: "/dashboard" as Route,
} as const;
