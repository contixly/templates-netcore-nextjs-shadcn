import type { ApiFailure } from "@/src/lib/api/result";

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return typeof value === "object" && value !== null
    ? (value as Record<string, unknown>)
    : undefined;
}

function nonEmptyString(value: unknown): string | undefined {
  return typeof value === "string" && value.trim() ? value : undefined;
}

export function normalizeApiFailure(
  error: unknown,
  response?: Response,
): ApiFailure {
  if (!response) {
    return {
      kind: "network",
      code: "api_unavailable",
    };
  }

  const problem = asRecord(error);
  const traceId = nonEmptyString(problem?.traceId);

  return {
    kind: "problem",
    code: nonEmptyString(problem?.code) ?? "api_problem",
    status: response.status,
    ...(traceId ? { traceId } : {}),
  };
}
