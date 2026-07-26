import "server-only";

import { headers } from "next/headers";

import type { ForwardedApiHeaders } from "@/src/lib/api/server/client";

export async function readForwardedApiHeaders(): Promise<ForwardedApiHeaders> {
  const incoming = await headers();
  const cookie = incoming.get("cookie") ?? undefined;
  const correlationId = incoming.get("x-correlation-id") ?? undefined;
  return {
    ...(cookie ? { cookie } : {}),
    ...(correlationId ? { correlationId } : {}),
  };
}
