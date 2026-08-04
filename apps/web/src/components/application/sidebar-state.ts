export const SIDEBAR_PREFERENCE_COOKIE = "template.sidebar";

const SIDEBAR_PREFERENCE_MAX_AGE = 60 * 60 * 24 * 30;

export function parseSidebarPreference(cookieHeader?: string | null): boolean {
  if (!cookieHeader) {
    return false;
  }

  const value = cookieHeader.includes("=")
    ? cookieHeader
        .split(";")
        .map((part) => part.trim())
        .find((part) => part.startsWith(`${SIDEBAR_PREFERENCE_COOKIE}=`))
        ?.slice(SIDEBAR_PREFERENCE_COOKIE.length + 1)
    : cookieHeader;

  return value === "open";
}

export function serializeSidebarPreference(open: boolean): string {
  return `${SIDEBAR_PREFERENCE_COOKIE}=${open ? "open" : "closed"}; Path=/; Max-Age=${SIDEBAR_PREFERENCE_MAX_AGE}; SameSite=Lax`;
}
