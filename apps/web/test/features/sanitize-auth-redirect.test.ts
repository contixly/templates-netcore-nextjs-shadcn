import {
  authLoginUrl,
  sanitizeAuthRedirect,
} from "@/src/features/authentication/sanitize-auth-redirect";

describe("authentication redirect policy", () => {
  it.each([
    ["/dashboard", "/dashboard"],
    ["/settings?tab=profile", "/settings?tab=profile"],
    ["https://evil.test", "/dashboard"],
    ["//evil.test", "/dashboard"],
    ["/api/v1/auth/session", "/dashboard"],
    ["/auth/login", "/dashboard"],
    ["/auth/login?redirect=/dashboard", "/dashboard"],
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
