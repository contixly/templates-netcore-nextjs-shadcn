import type { Route } from "next";

import { collaborationRoutes } from "@/src/features/collaboration/collaboration-routes";
import { apiKeyRoutes } from "@/src/features/api-keys/api-key-routes";

export const accountRoutes = {
  root: "/user" as Route,
  profile: "/user/profile" as Route,
  connections: "/user/connections" as Route,
  security: "/user/security" as Route,
  invitations: collaborationRoutes.accountInvitations,
  apiKeys: apiKeyRoutes.personal,
  danger: "/user/danger" as Route,
} as const;
