import type { Route } from "next";

export const accountRoutes = {
  root: "/user" as Route,
  profile: "/user/profile" as Route,
  connections: "/user/connections" as Route,
  security: "/user/security" as Route,
  danger: "/user/danger" as Route,
} as const;
