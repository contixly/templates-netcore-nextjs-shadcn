import "server-only";

import { cache } from "react";

import { loadAuthSession } from "@/src/lib/api/auth/load-auth-session";
import type { AuthSessionResult } from "@/src/lib/api/result";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

async function loadServerAuthSessionUncached(): Promise<AuthSessionResult> {
  const client = createServerApiClient(await readForwardedApiHeaders());
  if (!client.ok) {
    return { ok: false, failure: client.failure };
  }

  return loadAuthSession(client.client, { suppressSlidingRenewal: true });
}

export const loadServerAuthSession = cache(loadServerAuthSessionUncached);
