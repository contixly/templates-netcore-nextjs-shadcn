import type {
  ApiKeyResponse,
  CreateApiKeyRequest,
} from "@/src/lib/api/generated/types.gen";

export type ApiKeyPresetId = CreateApiKeyRequest["presetIds"][number];
export type ApiKeyExpiry = CreateApiKeyRequest["expiresIn"];
export type ApiKeyRateLimitWindow = CreateApiKeyRequest["rateLimitWindow"];

export const API_KEY_PRESET_OPTIONS = [
  { id: "basic-read", scopes: ["basic:read"] },
  { id: "organization-read", scopes: ["organization:read"] },
  {
    id: "organization-members-read",
    scopes: ["organization:read", "member:read"],
  },
  {
    id: "organization-teams-read",
    scopes: ["organization:read", "team:read"],
  },
  {
    id: "organization-team-members-read",
    scopes: ["organization:read", "team:read", "teamMember:read"],
  },
  {
    id: "organization-read-all",
    scopes: [
      "organization:read",
      "member:read",
      "team:read",
      "teamMember:read",
    ],
  },
] as const satisfies readonly Readonly<{
  id: ApiKeyPresetId;
  scopes: readonly ApiKeyResponse["scopes"][number][];
}>[];

export const API_KEY_EXPIRY_OPTIONS = [
  "never",
  "7d",
  "30d",
  "90d",
  "365d",
] as const satisfies readonly ApiKeyExpiry[];

export const API_KEY_RATE_LIMIT_WINDOW_OPTIONS = [
  "1m",
  "1h",
  "1d",
] as const satisfies readonly ApiKeyRateLimitWindow[];

export const PERSONAL_API_KEY_DEFAULTS = {
  presetIds: ["basic-read"],
  expiresIn: "30d",
  rateLimitEnabled: true,
  rateLimitMax: 1000,
  rateLimitWindow: "1h",
} as const satisfies Omit<CreateApiKeyRequest, "name">;

export const ORGANIZATION_API_KEY_DEFAULTS = {
  ...PERSONAL_API_KEY_DEFAULTS,
  presetIds: ["organization-read-all"],
} as const satisfies Omit<CreateApiKeyRequest, "name">;

export function apiKeyPresetIdsForScopes(
  scopes: readonly ApiKeyResponse["scopes"][number][],
): ApiKeyPresetId[] {
  const remaining = new Set(scopes);
  const result: ApiKeyPresetId[] = [];
  if (remaining.delete("basic:read")) result.push("basic-read");

  const hasAllOrganizationReads = [
    "organization:read",
    "member:read",
    "team:read",
    "teamMember:read",
  ].every((scope) => remaining.has(scope as ApiKeyResponse["scopes"][number]));
  if (hasAllOrganizationReads) {
    return [...result, "organization-read-all"];
  }

  const organizationPresetStart = result.length;
  if (remaining.has("member:read")) result.push("organization-members-read");
  if (remaining.has("teamMember:read")) {
    result.push("organization-team-members-read");
  } else if (remaining.has("team:read")) {
    result.push("organization-teams-read");
  }
  if (
    remaining.has("organization:read") &&
    result.length === organizationPresetStart
  ) {
    result.push("organization-read");
  }
  return result;
}

export function apiKeyScopesForPresetIds(
  presetIds: readonly ApiKeyPresetId[],
): ApiKeyResponse["scopes"] {
  const selected = new Set(presetIds);
  const scopes = new Set<ApiKeyResponse["scopes"][number]>();
  for (const option of API_KEY_PRESET_OPTIONS) {
    if (selected.has(option.id)) {
      for (const scope of option.scopes) scopes.add(scope);
    }
  }
  return [
    "basic:read",
    "organization:read",
    "member:read",
    "team:read",
    "teamMember:read",
  ].filter((scope): scope is ApiKeyResponse["scopes"][number] =>
    scopes.has(scope as ApiKeyResponse["scopes"][number]),
  );
}
