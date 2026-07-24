import type { Route } from "next";

import { authenticationRoutes } from "@/src/features/authentication/authentication-routes";

export function sanitizeAuthRedirect(
  value: string | string[] | undefined,
): Route {
  const candidate = Array.isArray(value) ? value[0] : value;
  if (
    !candidate ||
    !candidate.startsWith("/") ||
    candidate.startsWith("//") ||
    candidate === authenticationRoutes.login ||
    candidate.startsWith(`${authenticationRoutes.login}?`) ||
    candidate.startsWith("/auth/") ||
    candidate === "/api" ||
    candidate.startsWith("/api/")
  ) {
    return authenticationRoutes.dashboard;
  }

  return candidate as Route;
}

export function authLoginUrl(redirectPath: string): Route {
  const safe = sanitizeAuthRedirect(redirectPath);
  return `${authenticationRoutes.login}?redirect=${encodeURIComponent(safe)}` as Route;
}
