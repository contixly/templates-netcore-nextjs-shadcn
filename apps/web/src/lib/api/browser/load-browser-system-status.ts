"use client";

import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import { loadSystemStatus } from "@/src/lib/api/load-system-status";
import type { SystemStatusResult } from "@/src/lib/api/result";

export function loadBrowserSystemStatus(
  signal?: AbortSignal,
): Promise<SystemStatusResult> {
  return loadSystemStatus(createBrowserApiClient(), "browser", signal);
}
