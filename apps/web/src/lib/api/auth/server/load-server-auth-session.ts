import "server-only";

import { loadAuthSession } from "@/src/lib/api/auth/load-auth-session";
import type { AuthSessionResult } from "@/src/lib/api/result";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

export async function loadServerAuthSession(): Promise<AuthSessionResult> {
  const client = createServerApiClient(await readForwardedApiHeaders());
  if (!client.ok) {
    return { ok: false, failure: client.failure };
  }

  return loadAuthSession(client.client, { suppressSlidingRenewal: true });
}
