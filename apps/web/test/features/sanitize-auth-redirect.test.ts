import {
  authLoginUrl,
  sanitizeAuthRedirect,
} from "@/src/features/authentication/sanitize-auth-redirect";

describe("authentication redirect policy", () => {
  it.each([
    ["/dashboard", "/dashboard"],
    ["/settings?tab=profile", "/settings?tab=profile"],
    ["/safe/../dashboard?tab=profile", "/dashboard?tab=profile"],
    ["https://evil.test", "/dashboard"],
    ["//evil.test", "/dashboard"],
    ["/\\evil.test/path", "/dashboard"],
    ["/\\/evil.test/path", "/dashboard"],
    ["/%5c%5cevil.test/path", "/dashboard"],
    ["/\t/evil.test/path", "/dashboard"],
    ["/\n/evil.test/path", "/dashboard"],
    ["/\r/evil.test/path", "/dashboard"],
    ["/\u0000/evil.test/path", "/dashboard"],
    ["/%09/evil.test/path", "/dashboard"],
    ["/api/v1/auth/session", "/dashboard"],
    ["/api?x=1", "/dashboard"],
    ["/safe/../api/v1/auth/session", "/dashboard"],
    ["/auth/login", "/dashboard"],
    ["/auth/login?redirect=/dashboard", "/dashboard"],
    ["/safe/../auth/login", "/dashboard"],
    ["/safe/%2e%2e/auth/login", "/dashboard"],
    ["/%61uth/login", "/dashboard"],
    ["/%61pi/v1/auth/session", "/dashboard"],
    ["/auth\\login", "/dashboard"],
    ["dashboard", "/dashboard"],
    [undefined, "/dashboard"],
  ])("sanitizes %p to %p", (value, expected) => {
    expect(sanitizeAuthRedirect(value)).toBe(expected);
  });

  it("encodes the protected target into the login URL", () => {
    expect(authLoginUrl("/dashboard")).toBe(
      "/auth/login?redirect=%2Fdashboard",
    );
  });
});
