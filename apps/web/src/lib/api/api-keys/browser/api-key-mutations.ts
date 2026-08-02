"use client";

import type { ApiKeyOwner } from "@/src/features/api-keys/api-key-routes";
import { runCsrfMutation } from "@/src/lib/api/browser/run-csrf-mutation";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import {
  createOrganizationApiKey,
  createPersonalApiKey,
  listOrganizationApiKeys,
  listPersonalApiKeys,
  revokeOrganizationApiKey,
  revokePersonalApiKey,
  rotateOrganizationApiKey,
  rotatePersonalApiKey,
  updateOrganizationApiKey,
  updatePersonalApiKey,
} from "@/src/lib/api/generated/sdk.gen";
import type {
  ApiKeyPageResponse,
  ApiKeyRevocationResponse,
  ApiKeySecretResponse,
  ApiKeyResponse,
  CreateApiKeyRequest,
  ListPersonalApiKeysData,
  UpdateApiKeyRequest,
} from "@/src/lib/api/generated/types.gen";
import type { Client } from "@/src/lib/api/generated/client";
import type { ApiResult } from "@/src/lib/api/result";

export type ApiKeyListQuery = Readonly<
  NonNullable<ListPersonalApiKeysData["query"]>
>;

export async function listBrowserApiKeys(
  client: Client,
  owner: ApiKeyOwner,
  query: ApiKeyListQuery = {},
): Promise<ApiResult<ApiKeyPageResponse>> {
  try {
    const result =
      owner.kind === "personal"
        ? await listPersonalApiKeys({ client, cache: "no-store", query })
        : await listOrganizationApiKeys({
            client,
            cache: "no-store",
            path: { organizationId: owner.organizationId },
            query,
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

export function createBrowserApiKey(
  client: Client,
  owner: ApiKeyOwner,
  body: CreateApiKeyRequest,
): Promise<ApiResult<ApiKeySecretResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    owner.kind === "personal"
      ? createPersonalApiKey({
          client,
          body,
          headers: { "X-CSRF-TOKEN": csrfToken },
        })
      : createOrganizationApiKey({
          client,
          body,
          headers: { "X-CSRF-TOKEN": csrfToken },
          path: { organizationId: owner.organizationId },
        }),
  );
}

export function updateBrowserApiKey(
  client: Client,
  owner: ApiKeyOwner,
  apiKeyId: string,
  body: UpdateApiKeyRequest,
): Promise<ApiResult<ApiKeyResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    owner.kind === "personal"
      ? updatePersonalApiKey({
          client,
          body,
          headers: { "X-CSRF-TOKEN": csrfToken },
          path: { apiKeyId },
        })
      : updateOrganizationApiKey({
          client,
          body,
          headers: { "X-CSRF-TOKEN": csrfToken },
          path: { organizationId: owner.organizationId, apiKeyId },
        }),
  );
}

export function rotateBrowserApiKey(
  client: Client,
  owner: ApiKeyOwner,
  apiKeyId: string,
): Promise<ApiResult<ApiKeySecretResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    owner.kind === "personal"
      ? rotatePersonalApiKey({
          client,
          headers: { "X-CSRF-TOKEN": csrfToken },
          path: { apiKeyId },
        })
      : rotateOrganizationApiKey({
          client,
          headers: { "X-CSRF-TOKEN": csrfToken },
          path: { organizationId: owner.organizationId, apiKeyId },
        }),
  );
}

export function revokeBrowserApiKey(
  client: Client,
  owner: ApiKeyOwner,
  apiKeyId: string,
): Promise<ApiResult<ApiKeyRevocationResponse>> {
  return runCsrfMutation(client, (csrfToken) =>
    owner.kind === "personal"
      ? revokePersonalApiKey({
          client,
          headers: { "X-CSRF-TOKEN": csrfToken },
          path: { apiKeyId },
        })
      : revokeOrganizationApiKey({
          client,
          headers: { "X-CSRF-TOKEN": csrfToken },
          path: { organizationId: owner.organizationId, apiKeyId },
        }),
  );
}
