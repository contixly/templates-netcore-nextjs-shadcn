export type MobileTableContainment = Readonly<{
  clientWidth: number;
  containerLeft: number;
  containerRight: number;
  overflowX: string;
  scrollWidth: number;
  tableLeft: number;
  tableRight: number;
  tableWidth: number;
  viewportWidth: number;
}>;

function unique(values: readonly string[]) {
  return [...new Set(values)];
}

function hasPositivePersistenceClaim(text: string) {
  return text.split(/[\n.!?]+/u).some((clause) => {
    const persistenceTerm = /\b(?:sav(?:e|ed|es|ing)|persist(?:ed|s|ing)?)\b/iu;
    if (!persistenceTerm.test(clause)) {
      return false;
    }
    const positiveText = clause.replace(
      /\b(?:(?:(?:is|are|was|were|will|would|can|could|should|do|does|did|has|have|had)\s+)?(?:not|never)|(?:aren't|weren't|isn't|wasn't|won't|wouldn't|can't|couldn't|shouldn't|don't|doesn't|didn't|hasn't|haven't|hadn't))\s+(?:(?:be|been|being|have\s+been)\s+)?(?:sav(?:e|ed|es|ing)|persist(?:ed|s|ing)?)\b/giu,
      "",
    );
    if (!persistenceTerm.test(positiveText)) {
      return false;
    }

    return (
      /\b(?:dashboard|demo)\s+(?:changes|edits|layout|state|settings|preferences|configuration)\b/iu.test(
        positiveText,
      ) ||
      /\b(?:saved|persisted)\b.{0,40}\b(?:server|database|account|session)\b/iu.test(
        positiveText,
      ) ||
      /\b(?:server|database|account|session)\b.{0,40}\b(?:sav(?:e|ed|es|ing)|persist(?:ed|s|ing)?)\b/iu.test(
        positiveText,
      )
    );
  });
}

export function findSensitiveShellDisclosures(
  text: string,
  configuredSecrets: readonly string[],
) {
  const disclosures: string[] = [];

  if (
    configuredSecrets.some(
      (secret) => secret.length > 0 && text.includes(secret),
    )
  ) {
    disclosures.push("configured secret");
  }
  if (/\b(?:password|passphrase)\b\s*[:=]\s*\S+/iu.test(text)) {
    disclosures.push("password value");
  }
  if (
    /["']?\b(?:(?:next|previous|continuation|page)[-_ ]?)?cursor\b["']?\s*[:=]\s*["']?[A-Za-z0-9+/_=-]{8,}/iu.test(
      text,
    )
  ) {
    disclosures.push("opaque cursor");
  }
  if (
    /\bProblemDetails\b|\btrace[-_ ]?id\b\s*[:=]|\/api\/v1(?:\/|\b)|\bHTTP\s+[45]\d{2}\b|\bAPI\s+(?:error|response)\b/iu.test(
      text,
    )
  ) {
    disclosures.push("raw API error");
  }
  if (
    /["']?\b(?:authorization|cookie|set-cookie|(?:x-)?(?:csrf|xsrf)-token)\b["']?\s*[:=]|__(?:Host|Secure)-|\bBearer\s+[A-Za-z0-9._~+/-]{8,}|\b(?:auth|session)[-_ ]?cookie\b\s*[:=]/iu.test(
      text,
    )
  ) {
    disclosures.push("authentication material");
  }
  if (hasPositivePersistenceClaim(text)) {
    disclosures.push("persistence claim");
  }

  return unique(disclosures);
}

export function findMobileTableContainmentIssues(
  measurements: MobileTableContainment,
) {
  const issues: string[] = [];
  const tolerance = 1;

  if (measurements.containerLeft < 0) {
    issues.push("container starts outside viewport");
  }
  if (measurements.containerRight > measurements.viewportWidth) {
    issues.push("container ends outside viewport");
  }
  if (measurements.overflowX !== "auto") {
    issues.push("container does not clip horizontal overflow");
  }
  if (measurements.scrollWidth <= measurements.clientWidth) {
    issues.push("table does not overflow");
  }
  if (measurements.tableWidth <= measurements.clientWidth) {
    issues.push("table is not wider than its container");
  }
  if (measurements.tableLeft < measurements.containerLeft - tolerance) {
    issues.push("table starts before its container");
  }
  if (measurements.tableLeft > measurements.containerRight + tolerance) {
    issues.push("table starts after its container");
  }
  if (measurements.tableRight <= measurements.containerRight) {
    issues.push("table bounds do not demonstrate contained overflow");
  }
  if (
    measurements.tableRight >
    measurements.containerLeft + measurements.scrollWidth + tolerance
  ) {
    issues.push("table exceeds its scrollable container bounds");
  }

  return unique(issues);
}
