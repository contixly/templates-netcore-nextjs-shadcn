import type { ApiFailure } from "@/src/lib/api/result";

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return typeof value === "object" && value !== null
    ? (value as Record<string, unknown>)
    : undefined;
}

function nonEmptyString(value: unknown): string | undefined {
  return typeof value === "string" && value.trim() ? value : undefined;
}

function nonEmptyStrings(value: unknown): string[] | undefined {
  if (
    !Array.isArray(value) ||
    value.some((item) => nonEmptyString(item) === undefined)
  ) {
    return undefined;
  }

  return value.map((item) => item as string);
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
  const code = nonEmptyString(problem?.code) ?? "api_problem";
  const domainAcknowledgement =
    code === "member_domain_acknowledgement_required"
      ? {
          email: nonEmptyString(problem?.email),
          emailDomain: nonEmptyString(problem?.emailDomain),
          allowedEmailDomains: nonEmptyStrings(problem?.allowedEmailDomains),
        }
      : undefined;

  return {
    kind: "problem",
    code,
    status: response.status,
    ...(traceId ? { traceId } : {}),
    ...(domainAcknowledgement?.email
      ? { email: domainAcknowledgement.email }
      : {}),
    ...(domainAcknowledgement?.emailDomain
      ? { emailDomain: domainAcknowledgement.emailDomain }
      : {}),
    ...(domainAcknowledgement?.allowedEmailDomains
      ? { allowedEmailDomains: domainAcknowledgement.allowedEmailDomains }
      : {}),
  };
}
