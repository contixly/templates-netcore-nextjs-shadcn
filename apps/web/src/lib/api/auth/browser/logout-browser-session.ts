"use client";

import { getAuthCsrfToken } from "@/src/lib/api/auth/browser/get-auth-csrf";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { logout } from "@/src/lib/api/generated";
import type { Client } from "@/src/lib/api/generated/client";
import type { AuthSessionResult } from "@/src/lib/api/result";

export async function logoutBrowserSession(
  client: Client,
): Promise<AuthSessionResult> {
  const csrf = await getAuthCsrfToken(client);
  if (!csrf.ok) {
    return csrf;
  }

  try {
    const result = await logout({
      client,
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
