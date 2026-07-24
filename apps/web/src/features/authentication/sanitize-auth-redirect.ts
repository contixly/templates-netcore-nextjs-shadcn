import type { Route } from "next";

import { authenticationRoutes } from "@/src/features/authentication/authentication-routes";

const redirectBaseUrl = new URL("https://auth-redirect.invalid");
const unsafeUrlCharacters = /[\u0000-\u001f\u007f\\]/;

function isExcludedPath(pathname: string): boolean {
  const normalizedPathname = pathname.toLowerCase();
  return (
    normalizedPathname === "/auth" ||
    normalizedPathname.startsWith("/auth/") ||
    normalizedPathname === "/api" ||
    normalizedPathname.startsWith("/api/")
  );
}

function normalizedDecodedUrl(url: URL): URL | undefined {
  let decodedPathname = url.pathname;

  try {
    for (
      let remaining = decodedPathname.length;
      remaining > 0;
      remaining -= 1
    ) {
      const decoded = decodeURIComponent(decodedPathname);
      if (decoded === decodedPathname) {
        break;
      }
      decodedPathname = decoded;
    }
  } catch {
    return undefined;
  }

  if (unsafeUrlCharacters.test(decodedPathname)) {
    return undefined;
  }

  const normalized = new URL(decodedPathname, redirectBaseUrl);
  return normalized.origin === redirectBaseUrl.origin ? normalized : undefined;
}

export function sanitizeAuthRedirect(
  value: string | string[] | undefined,
): Route {
  const candidate = Array.isArray(value) ? value[0] : value;
  if (
    !candidate ||
    !candidate.startsWith("/") ||
    unsafeUrlCharacters.test(candidate)
  ) {
    return authenticationRoutes.dashboard;
  }

  try {
    const parsed = new URL(candidate, redirectBaseUrl);
    const decoded = normalizedDecodedUrl(parsed);
    if (
      parsed.origin !== redirectBaseUrl.origin ||
      !decoded ||
      isExcludedPath(decoded.pathname)
    ) {
      return authenticationRoutes.dashboard;
    }

    return `${parsed.pathname}${parsed.search}${parsed.hash}` as Route;
  } catch {
    return authenticationRoutes.dashboard;
  }
}

export function authLoginUrl(redirectPath: string): Route {
  const safe = sanitizeAuthRedirect(redirectPath);
  return `${authenticationRoutes.login}?redirect=${encodeURIComponent(safe)}` as Route;
}
