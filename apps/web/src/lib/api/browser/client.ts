"use client";

import { createClient, type Client } from "@/src/lib/api/generated/client";

export function createBrowserApiClient(): Client {
  return createClient({
    baseUrl: "",
    credentials: "same-origin",
  });
}
