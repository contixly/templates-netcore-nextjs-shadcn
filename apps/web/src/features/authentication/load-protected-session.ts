import "server-only";

import { redirect } from "next/navigation";

import { authLoginUrl } from "@/src/features/authentication/sanitize-auth-redirect";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import type { AuthSessionResult } from "@/src/lib/api/result";

export async function loadProtectedSession(
  redirectPath: string,
): Promise<AuthSessionResult> {
  const session = await loadServerAuthSession();

  if (session.ok && session.data.authenticated === false) {
    redirect(authLoginUrl(redirectPath));
  }

  return session;
}
