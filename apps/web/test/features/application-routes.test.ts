import { applicationRoutes } from "@/src/features/application/application-routes";

describe("applicationRoutes", () => {
  it("keeps the only iteration-2 route unprefixed", () => {
    expect(applicationRoutes).toEqual({ home: "/" });
  });
});
