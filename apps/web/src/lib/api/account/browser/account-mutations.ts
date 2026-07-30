"use client";

import { runCsrfMutation } from "@/src/lib/api/browser/run-csrf-mutation";
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

export function updateBrowserAccountProfile(
  client: Client,
  body: UpdateProfileRequest,
): Promise<ApiResult<AccountResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
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
  return runCsrfMutation(client, (csrfToken) =>
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
  return runCsrfMutation(client, (csrfToken) =>
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
  return runCsrfMutation(client, (csrfToken) =>
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
  return runCsrfMutation(client, (csrfToken) =>
    deleteAccount({
      client,
      body,
      headers: { "X-CSRF-TOKEN": csrfToken },
    }),
  );
}
