import { applicationRoutes } from "@/src/features/application/application-routes";

describe("applicationRoutes", () => {
  it("keeps browser routes unprefixed", () => {
    expect(applicationRoutes).toEqual({
      home: "/",
      login: "/auth/login",
      dashboard: "/dashboard",
    });
  });
});
