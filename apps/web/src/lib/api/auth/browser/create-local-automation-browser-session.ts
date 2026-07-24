"use client";

import { getAuthCsrfToken } from "@/src/lib/api/auth/browser/get-auth-csrf";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { createLocalAutomationScenario } from "@/src/lib/api/generated";
import type { Client } from "@/src/lib/api/generated/client";
import type { LocalAutomationScenarioResult } from "@/src/lib/api/result";

export async function createLocalAutomationBrowserSession(
  client: Client,
): Promise<LocalAutomationScenarioResult> {
  const csrf = await getAuthCsrfToken(client);
  if (!csrf.ok) {
    return csrf;
  }

  try {
    const result = await createLocalAutomationScenario({
      client,
      body: {},
      headers: { "X-CSRF-TOKEN": csrf.data },
    });
    return result.data !== undefined
      ? { ok: true, data: result.data.data }
      : {
          ok: false,
          failure: normalizeApiFailure(result.error, result.response),
        };
  } catch (error) {
    return { ok: false, failure: normalizeApiFailure(error) };
  }
}
