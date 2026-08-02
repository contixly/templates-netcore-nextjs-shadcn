import {
  API_KEY_EXPIRY_OPTIONS,
  API_KEY_PRESET_OPTIONS,
  API_KEY_RATE_LIMIT_WINDOW_OPTIONS,
  PERSONAL_API_KEY_DEFAULTS,
  apiKeyPresetIdsForScopes,
  apiKeyScopesForPresetIds,
} from "@/src/features/api-keys/api-key-options";

describe("API key closed options", () => {
  it("exposes only the contract presets with their literal expanded scopes", () => {
    expect(API_KEY_PRESET_OPTIONS).toEqual([
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
    ]);
  });

  it("keeps expiry and rate-window choices closed to generated contract values", () => {
    expect(API_KEY_EXPIRY_OPTIONS).toEqual([
      "never",
      "7d",
      "30d",
      "90d",
      "365d",
    ]);
    expect(API_KEY_RATE_LIMIT_WINDOW_OPTIONS).toEqual(["1m", "1h", "1d"]);
  });

  it("starts personal creation with the approved least-privilege defaults", () => {
    expect(PERSONAL_API_KEY_DEFAULTS).toEqual({
      presetIds: ["basic-read"],
      expiresIn: "30d",
      rateLimitEnabled: true,
      rateLimitMax: 1000,
      rateLimitWindow: "1h",
    });
  });

  it("round-trips combined personal and organization scopes without arbitrary input", () => {
    const presets = apiKeyPresetIdsForScopes([
      "basic:read",
      "organization:read",
    ]);

    expect(presets).toEqual(["basic-read", "organization-read"]);
    expect(apiKeyScopesForPresetIds(presets)).toEqual([
      "basic:read",
      "organization:read",
    ]);
  });
});
