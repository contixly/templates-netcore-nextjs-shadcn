"use client";

import { loadAuthSession } from "@/src/lib/api/auth/load-auth-session";
import type { AuthSessionResult } from "@/src/lib/api/result";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";

export function refreshBrowserAuthSession(): Promise<AuthSessionResult> {
  return loadAuthSession(createBrowserApiClient());
}
