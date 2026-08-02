import type { ApiFailure } from "@/src/lib/api/result";

export type ApiKeyFailureMessage =
  | "antiforgery_failed"
  | "api_key_not_found"
  | "api_key_permission_denied"
  | "api_key_update_unchanged"
  | "validation_failed"
  | "generic";

const knownCodes = new Set<ApiKeyFailureMessage>([
  "antiforgery_failed",
  "api_key_not_found",
  "api_key_permission_denied",
  "api_key_update_unchanged",
  "validation_failed",
]);

export function apiKeyFailureMessage(
  failure: ApiFailure,
): ApiKeyFailureMessage {
  return failure.kind === "problem" &&
    knownCodes.has(failure.code as ApiKeyFailureMessage)
    ? (failure.code as ApiKeyFailureMessage)
    : "generic";
}

export function apiKeyIdentityMismatchFailure(): ApiFailure {
  return {
    kind: "problem",
    code: "api_response_identity_mismatch",
    status: 502,
  };
}
