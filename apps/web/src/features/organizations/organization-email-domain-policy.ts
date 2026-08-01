const maximumDomainLength = 253;
const maximumLabelLength = 63;
const asciiDomainLabel = /^[a-z0-9-]+$/;
const whitespace = /\p{White_Space}/u;

function normalizeDomain(value: string): string | null {
  const normalized = value.trim().toLowerCase().replace(/^@/, "");
  if (normalized.length === 0 || normalized.length > maximumDomainLength) {
    return null;
  }

  const labels = normalized.split(".");
  if (labels.length < 2) {
    return null;
  }
  if (
    labels.some(
      (label) =>
        label.length === 0 ||
        label.length > maximumLabelLength ||
        label.startsWith("-") ||
        label.endsWith("-") ||
        !asciiDomainLabel.test(label),
    )
  ) {
    return null;
  }

  return normalized;
}

function extractEmailDomain(email: string): string | null {
  const normalizedEmail = email.trim().toLowerCase();
  const separator = normalizedEmail.indexOf("@");
  if (
    whitespace.test(normalizedEmail) ||
    separator < 1 ||
    separator !== normalizedEmail.lastIndexOf("@") ||
    separator === normalizedEmail.length - 1
  ) {
    return null;
  }

  return normalizeDomain(normalizedEmail.slice(separator + 1));
}

export function evaluateOrganizationEmailDomainEligibility(
  email: string,
  allowedEmailDomains: readonly string[],
): Readonly<{ emailDomain: string | null; isAllowed: boolean }> {
  const emailDomain = extractEmailDomain(email);
  return {
    emailDomain,
    isAllowed:
      allowedEmailDomains.length === 0 ||
      (emailDomain !== null && allowedEmailDomains.includes(emailDomain)),
  };
}
