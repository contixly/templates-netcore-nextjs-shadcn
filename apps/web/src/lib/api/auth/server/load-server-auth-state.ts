import "server-only";

import type {
  AuthCapabilitiesResponse,
  AuthSessionResponse,
} from "@/src/lib/api/generated";
import { loadAuthCapabilities } from "@/src/lib/api/auth/load-auth-capabilities";
import { loadAuthSession } from "@/src/lib/api/auth/load-auth-session";
import type { ApiResult } from "@/src/lib/api/result";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

export type AuthPageState = Readonly<{
  capabilities: AuthCapabilitiesResponse;
  session: AuthSessionResponse;
}>;

export async function loadServerAuthState(): Promise<ApiResult<AuthPageState>> {
  const client = createServerApiClient(await readForwardedApiHeaders());
  if (!client.ok) {
    return { ok: false, failure: client.failure };
  }

  const [capabilities, session] = await Promise.all([
    loadAuthCapabilities(client.client),
    loadAuthSession(client.client),
  ]);
  if (!capabilities.ok) {
    return capabilities;
  }
  if (!session.ok) {
    return session;
  }

  return {
    ok: true,
    data: {
      capabilities: capabilities.data,
      session: session.data,
    },
  };
}
