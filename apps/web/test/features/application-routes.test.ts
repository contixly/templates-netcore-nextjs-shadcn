import { applicationRoutes } from "@/src/features/application/application-routes";
import { authenticationRoutes } from "@/src/features/authentication/authentication-routes";

describe("applicationRoutes", () => {
  it("keeps browser routes unprefixed", () => {
    expect(applicationRoutes).toEqual({
      home: "/",
      login: "/auth/login",
      authError: "/auth/error",
      dashboard: "/dashboard",
      welcome: "/welcome",
      workspaces: "/workspaces",
    });
  });

  it("exposes the authentication error destination as a typed UI route", () => {
    expect(authenticationRoutes).toEqual({
      login: "/auth/login",
      error: "/auth/error",
      dashboard: "/dashboard",
    });
  });
});
