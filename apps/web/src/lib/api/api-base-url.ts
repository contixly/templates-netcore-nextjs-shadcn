export type ApiConfigurationCode =
  "api_configuration_missing" | "api_configuration_invalid";

export type ApiBaseUrlResult =
  { ok: true; value: string } | { ok: false; code: ApiConfigurationCode };

export function resolveApiBaseUrl(value: string | undefined): ApiBaseUrlResult {
  const candidate = value?.trim();

  if (!candidate) {
    return { ok: false, code: "api_configuration_missing" };
  }

  try {
    const url = new URL(candidate);
    const hasOriginOnly =
      (url.protocol === "http:" || url.protocol === "https:") &&
      !url.username &&
      !url.password &&
      !url.search &&
      !url.hash &&
      url.pathname === "/";

    return hasOriginOnly
      ? { ok: true, value: url.origin }
      : { ok: false, code: "api_configuration_invalid" };
  } catch {
    return { ok: false, code: "api_configuration_invalid" };
  }
}
