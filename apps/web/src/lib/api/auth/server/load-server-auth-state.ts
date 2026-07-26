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
  const forwarded = await readForwardedApiHeaders();
  const capabilitiesClient = createServerApiClient({
    ...(forwarded.correlationId
      ? { correlationId: forwarded.correlationId }
      : {}),
  });
  if (!capabilitiesClient.ok) {
    return { ok: false, failure: capabilitiesClient.failure };
  }

  const sessionClient = createServerApiClient(forwarded);
  if (!sessionClient.ok) {
    return { ok: false, failure: sessionClient.failure };
  }

  const [capabilities, session] = await Promise.all([
    loadAuthCapabilities(capabilitiesClient.client),
    loadAuthSession(sessionClient.client, { suppressSlidingRenewal: true }),
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
