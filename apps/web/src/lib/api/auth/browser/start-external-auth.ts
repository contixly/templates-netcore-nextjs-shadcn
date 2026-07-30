"use client";

import type { Route } from "next";

import { getAuthCsrfToken } from "@/src/lib/api/auth/browser/get-auth-csrf";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import {
  challengeExternalAuth,
  type AuthProviderResponse,
  type ExternalAuthChallengeResponse,
  type ExternalAuthIntent,
} from "@/src/lib/api/generated";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { ApiResult } from "@/src/lib/api/result";

export type ExternalProvider = AuthProviderResponse["id"];

export async function startExternalAuth(
  input: Readonly<{
    provider: ExternalProvider;
    intent: ExternalAuthIntent;
    returnUrl: Route;
  }>,
): Promise<ApiResult<ExternalAuthChallengeResponse>> {
  const client = createBrowserApiClient();
  const csrf = await getAuthCsrfToken(client);
  if (!csrf.ok) {
    return csrf;
  }

  try {
    const result = await challengeExternalAuth({
      client,
      body: {
        intent: input.intent,
        returnUrl: input.returnUrl,
      },
      headers: { "X-CSRF-TOKEN": csrf.data },
      path: { provider: input.provider },
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
