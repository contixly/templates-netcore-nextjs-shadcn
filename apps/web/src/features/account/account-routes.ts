import type { Route } from "next";

import { collaborationRoutes } from "@/src/features/collaboration/collaboration-routes";

export const accountRoutes = {
  root: "/user" as Route,
  profile: "/user/profile" as Route,
  connections: "/user/connections" as Route,
  security: "/user/security" as Route,
  invitations: collaborationRoutes.accountInvitations,
  danger: "/user/danger" as Route,
} as const;
