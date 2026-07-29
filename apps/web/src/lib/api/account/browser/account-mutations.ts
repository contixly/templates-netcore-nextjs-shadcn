"use client";

import { getAuthCsrfToken } from "@/src/lib/api/auth/browser/get-auth-csrf";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import {
  deleteAccount,
  disconnectAccountProvider,
  revokeAccountSession,
  revokeOtherAccountSessions,
  updateAccountProfile,
  type AccountConnectionResponse,
  type AccountDeletionResponse,
  type AccountDisconnectionResponse,
  type AccountResponse,
  type AccountSessionRevocationResponse,
  type AccountSessionsRevocationResponse,
  type DeleteAccountRequest,
  type UpdateProfileRequest,
} from "@/src/lib/api/generated";
import type { Client } from "@/src/lib/api/generated/client";
import type { ApiResult } from "@/src/lib/api/result";

type AccountMutationResponse<T> = Readonly<{
  data?: Readonly<{ data: T }>;
  error?: unknown;
  response?: Response;
}>;

async function runAccountMutation<T>(
  client: Client,
  operation: (csrfToken: string) => Promise<AccountMutationResponse<T>>,
): Promise<ApiResult<T>> {
  const csrf = await getAuthCsrfToken(client);
  if (!csrf.ok) {
    return csrf;
  }

  try {
    const result = await operation(csrf.data);
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

export function updateBrowserAccountProfile(
  client: Client,
  body: UpdateProfileRequest,
): Promise<ApiResult<AccountResponse>> {
  return runAccountMutation(client, (csrfToken) =>
    updateAccountProfile({
      client,
      body,
      headers: { "X-CSRF-TOKEN": csrfToken },
    }),
  );
}

export function disconnectBrowserAccountProvider(
  client: Client,
  provider: AccountConnectionResponse["provider"],
): Promise<ApiResult<AccountDisconnectionResponse>> {
  return runAccountMutation(client, (csrfToken) =>
    disconnectAccountProvider({
      client,
      headers: { "X-CSRF-TOKEN": csrfToken },
      path: { provider },
    }),
  );
}

export function revokeBrowserAccountSession(
  client: Client,
  sessionId: string,
): Promise<ApiResult<AccountSessionRevocationResponse>> {
  return runAccountMutation(client, (csrfToken) =>
    revokeAccountSession({
      client,
      headers: { "X-CSRF-TOKEN": csrfToken },
      path: { sessionId },
    }),
  );
}

export function revokeOtherBrowserAccountSessions(
  client: Client,
): Promise<ApiResult<AccountSessionsRevocationResponse>> {
  return runAccountMutation(client, (csrfToken) =>
    revokeOtherAccountSessions({
      client,
      headers: { "X-CSRF-TOKEN": csrfToken },
    }),
  );
}

export function deleteBrowserAccount(
  client: Client,
  body: DeleteAccountRequest,
): Promise<ApiResult<AccountDeletionResponse>> {
  return runAccountMutation(client, (csrfToken) =>
    deleteAccount({
      client,
      body,
      headers: { "X-CSRF-TOKEN": csrfToken },
    }),
  );
}
