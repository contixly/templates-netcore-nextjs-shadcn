import type { APIRequestContext } from "@playwright/test";

import {
  createOrganizationApiKey,
  createPersonalApiKey,
  getApiKeyPrincipal,
  getMachineOrganization,
  getOrganizationMembers,
  getOrganizations,
  getTeamMembers,
  getTeams,
  listOrganizationApiKeys,
  listPersonalApiKeys,
  type ApiKeyMeResponse,
  type ApiKeyPageResponse,
  type ApiKeyResponse,
  type CreateApiKeyRequest,
  type MachineOrganizationDetailResponse,
  type OrganizationMemberPageResponse,
  type OrganizationPageResponse,
  type ProblemDetails,
  type TeamMemberPageResponse,
  type TeamPageResponse,
} from "../../src/lib/api/generated";
import { clientFor, csrf } from "./generated-auth-api";

export type GeneratedApiResponseMetadata = Readonly<{
  cacheControl: string | null;
  contentType: string | null;
  envelopeKeys: readonly string[];
  hasSetCookie: boolean;
  problemKeys: readonly string[];
  location: string | null;
  status: number;
}>;

export type GeneratedApiCall<T> = GeneratedApiResponseMetadata &
  (
    | Readonly<{ data: T; ok: true; problem?: never }>
    | Readonly<{ data?: never; ok: false; problem?: ProblemDetails }>
  );

type GeneratedFields<T> = Readonly<{
  data?: Readonly<{ data: T }>;
  error?: unknown;
  response?: Response;
}>;

function mediaType(value: string | null): string | null {
  return value?.split(";", 1)[0]?.trim().toLowerCase() ?? null;
}

function problemDetails(value: unknown): ProblemDetails | undefined {
  if (
    typeof value !== "object" ||
    value === null ||
    typeof Reflect.get(value, "code") !== "string" ||
    typeof Reflect.get(value, "status") !== "number"
  ) {
    return undefined;
  }
  return value as ProblemDetails;
}

function containsCredential(
  value: unknown,
  credential: string,
  seen: WeakSet<object>,
): boolean {
  if (!credential) return false;
  if (typeof value === "string") return value.includes(credential);
  if (typeof value !== "object" || value === null) return false;
  if (seen.has(value)) return false;
  seen.add(value);
  return Object.values(value).some((entry) =>
    containsCredential(entry, credential, seen),
  );
}

function rejectCredentialEcho(value: unknown, credential?: string) {
  if (
    credential &&
    containsCredential(value, credential, new WeakSet<object>())
  ) {
    throw new Error("Generated API response echoed a supplied credential.");
  }
}

function metadata(
  response: Response | undefined,
  envelopeKeys: readonly string[],
  problemKeys: readonly string[],
): GeneratedApiResponseMetadata {
  return {
    cacheControl: response?.headers.get("cache-control") ?? null,
    contentType: mediaType(response?.headers.get("content-type") ?? null),
    envelopeKeys,
    hasSetCookie: response?.headers.has("set-cookie") ?? false,
    location: response?.headers.get("location") ?? null,
    problemKeys,
    status: response?.status ?? 0,
  };
}

export type GeneratedCreatedApiKey = Readonly<{
  apiKey: ApiKeyResponse;
  response: GeneratedApiCall<ApiKeyResponse>;
  takeCredential: () => string;
}>;

export class SanitizedGeneratedApiError extends Error {
  constructor(
    operation: string,
    readonly status: number,
    readonly code: string,
  ) {
    super(`Generated API-key ${operation} failed with ${status} (${code}).`);
    this.name = "SanitizedGeneratedApiError";
  }
}

function createBody(
  options: Readonly<{
    name: string;
    presetIds: CreateApiKeyRequest["presetIds"];
  }>,
): CreateApiKeyRequest {
  return {
    name: options.name,
    presetIds: [...options.presetIds],
    expiresIn: "30d",
    rateLimitEnabled: true,
    rateLimitMax: 1000,
    rateLimitWindow: "1h",
  };
}

function requireCreatedApiKey(
  operation: string,
  result: GeneratedFields<ApiKeyResponse & { key: string }>,
): GeneratedCreatedApiKey {
  if (!result.data) {
    const problem = problemDetails(result.error);
    throw new SanitizedGeneratedApiError(
      operation,
      result.response?.status ?? 0,
      problem?.code ?? "unknown",
    );
  }

  const secretResponse = result.data.data;
  let credentialSlot = secretResponse.key;
  secretResponse.key = "";
  if (!/^(?:user|org)_[A-Za-z0-9_-]{43}$/u.test(credentialSlot)) {
    credentialSlot = "";
    throw new Error("Generated API-key create returned an invalid credential.");
  }
  const { key: clearedKey, ...apiKey } = secretResponse;
  if (clearedKey !== "") {
    throw new Error("Reveal-once credential was not cleared from SDK state.");
  }
  const response: GeneratedApiCall<ApiKeyResponse> = {
    ...metadata(result.response, Object.keys(result.data).sort(), []),
    data: apiKey,
    ok: true,
  };
  const takeCredential = () => {
    if (!credentialSlot) {
      throw new Error("Reveal-once credential was already taken.");
    }
    const credential = credentialSlot;
    credentialSlot = "";
    return credential;
  };
  return { apiKey, response, takeCredential };
}

function sanitize<T>(
  result: GeneratedFields<T>,
  credential?: string,
): GeneratedApiCall<T> {
  rejectCredentialEcho(result.data?.data ?? result.error, credential);
  if (result.data) {
    const envelopeKeys = Object.keys(result.data).sort();
    return {
      ...metadata(result.response, envelopeKeys, []),
      data: result.data.data,
      ok: true,
    };
  }

  const problem = problemDetails(result.error);
  return {
    ...metadata(
      result.response,
      [],
      problem ? Object.keys(problem).sort() : [],
    ),
    ok: false,
    ...(problem ? { problem } : {}),
  };
}

export async function callGeneratedApiKeyPrincipal(
  request: APIRequestContext,
  credential?: string,
): Promise<GeneratedApiCall<ApiKeyMeResponse>> {
  const result = await getApiKeyPrincipal({
    client: clientFor(request),
    cache: "no-store",
    ...(credential === undefined
      ? {}
      : { headers: { "x-api-key": credential } }),
  });
  return sanitize(result, credential);
}

export async function listGeneratedPersonalApiKeys(
  request: APIRequestContext,
  query: Readonly<{ cursor?: string; limit?: number }> = {},
): Promise<GeneratedApiCall<ApiKeyPageResponse>> {
  const result = await listPersonalApiKeys({
    client: clientFor(request),
    cache: "no-store",
    query,
  });
  return sanitize(result);
}

export async function createGeneratedPersonalKey(
  request: APIRequestContext,
  options: Readonly<{
    name: string;
    presetIds: CreateApiKeyRequest["presetIds"];
  }>,
): Promise<GeneratedCreatedApiKey> {
  const client = clientFor(request);
  const result = await createPersonalApiKey({
    client,
    body: createBody(options),
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  return requireCreatedApiKey("personal create", result);
}

export async function listGeneratedOrganizationApiKeys(
  request: APIRequestContext,
  organizationId: string,
  query: Readonly<{ cursor?: string; limit?: number }> = {},
): Promise<GeneratedApiCall<ApiKeyPageResponse>> {
  const result = await listOrganizationApiKeys({
    client: clientFor(request),
    cache: "no-store",
    path: { organizationId },
    query,
  });
  return sanitize(result);
}

export async function createGeneratedOrganizationKey(
  request: APIRequestContext,
  organizationId: string,
  options: Readonly<{
    name: string;
    presetIds: CreateApiKeyRequest["presetIds"];
  }>,
): Promise<GeneratedCreatedApiKey> {
  const client = clientFor(request);
  const result = await createOrganizationApiKey({
    client,
    body: createBody(options),
    headers: { "X-CSRF-TOKEN": await csrf(client) },
    path: { organizationId },
  });
  return requireCreatedApiKey("organization create", result);
}

function machineHeaders(credential: string) {
  return { "x-api-key": credential } as const;
}

export async function callGeneratedOrganizations(
  request: APIRequestContext,
  credential: string | undefined,
  query: Readonly<{ cursor?: string; limit?: number }> = {},
): Promise<GeneratedApiCall<OrganizationPageResponse>> {
  const result = await getOrganizations({
    client: clientFor(request),
    cache: "no-store",
    ...(credential === undefined
      ? {}
      : { headers: machineHeaders(credential) }),
    query,
  });
  return sanitize(result, credential);
}

export async function callGeneratedMachineOrganization(
  request: APIRequestContext,
  credential: string,
  organizationId: string,
): Promise<GeneratedApiCall<MachineOrganizationDetailResponse>> {
  const result = await getMachineOrganization({
    client: clientFor(request),
    cache: "no-store",
    headers: machineHeaders(credential),
    path: { organizationId },
  });
  return sanitize(result, credential);
}

export async function callGeneratedOrganizationMembers(
  request: APIRequestContext,
  credential: string,
  organizationId: string,
  query: Readonly<{ cursor?: string; limit?: number }> = {},
): Promise<GeneratedApiCall<OrganizationMemberPageResponse>> {
  const result = await getOrganizationMembers({
    client: clientFor(request),
    cache: "no-store",
    headers: machineHeaders(credential),
    path: { organizationId },
    query,
  });
  return sanitize(result, credential);
}

export async function callGeneratedTeams(
  request: APIRequestContext,
  credential: string,
  organizationId: string,
  query: Readonly<{ cursor?: string; limit?: number }> = {},
): Promise<GeneratedApiCall<TeamPageResponse>> {
  const result = await getTeams({
    client: clientFor(request),
    cache: "no-store",
    headers: machineHeaders(credential),
    path: { organizationId },
    query,
  });
  return sanitize(result, credential);
}

export async function callGeneratedTeamMembers(
  request: APIRequestContext,
  credential: string,
  organizationId: string,
  teamId: string,
  query: Readonly<{ cursor?: string; limit?: number }> = {},
): Promise<GeneratedApiCall<TeamMemberPageResponse>> {
  const result = await getTeamMembers({
    client: clientFor(request),
    cache: "no-store",
    headers: machineHeaders(credential),
    path: { organizationId, teamId },
    query,
  });
  return sanitize(result, credential);
}
