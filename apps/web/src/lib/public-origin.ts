const DEFAULT_PUBLIC_ORIGIN = "http://localhost:3000";

export function resolvePublicOrigin(
  value = process.env.APP_PUBLIC_ORIGIN,
): URL {
  const candidate = value?.trim() || DEFAULT_PUBLIC_ORIGIN;

  try {
    const url = new URL(candidate);
    const isOriginOnly =
      (url.protocol === "http:" || url.protocol === "https:") &&
      !url.username &&
      !url.password &&
      url.pathname === "/" &&
      !url.search &&
      !url.hash;

    if (isOriginOnly) {
      return new URL(url.origin);
    }
  } catch {
    // Report every invalid form through the same deployment-safe error.
  }

  throw new Error(
    "APP_PUBLIC_ORIGIN must be an absolute HTTP(S) origin without credentials, a path, query, or fragment.",
  );
}
