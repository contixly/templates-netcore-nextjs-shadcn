import "server-only";

import { loadSystemStatus } from "@/src/lib/api/load-system-status";
import type { SystemStatusResult } from "@/src/lib/api/result";
import { createServerApiClient } from "@/src/lib/api/server/client";

export function loadServerSystemStatus(): Promise<SystemStatusResult> {
  const client = createServerApiClient();

  if (!client.ok) {
    return Promise.resolve({
      ok: false,
      failure: client.failure,
    });
  }

  return loadSystemStatus(client.client, "ssr");
}
