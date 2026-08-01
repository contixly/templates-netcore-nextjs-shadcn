"use client";

import { runCsrfMutation } from "@/src/lib/api/browser/run-csrf-mutation";
import { confirmLocalAutomationEmail as confirmEmail } from "@/src/lib/api/generated/sdk.gen";
import type { AuthSessionResponse } from "@/src/lib/api/generated/types.gen";
import type { Client } from "@/src/lib/api/generated/client";
import type { ApiResult } from "@/src/lib/api/result";

export function confirmLocalAutomationEmail(
  client: Client,
): Promise<ApiResult<AuthSessionResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    confirmEmail({
      client,
      headers: { "X-CSRF-TOKEN": csrfToken },
    }),
  );
}
