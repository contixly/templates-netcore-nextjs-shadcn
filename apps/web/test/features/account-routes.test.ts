import type { Route } from "next";

import UserPage from "@/src/app/(site)/user/page";
import { accountRoutes } from "@/src/features/account/account-routes";

jest.mock("next/navigation", () => ({
  redirect: jest.fn((path: string) => {
    throw new Error(`NEXT_REDIRECT:${path}`);
  }),
}));

it("exposes the exact typed account routes including invitations", () => {
  expect(accountRoutes).toEqual({
    root: "/user",
    profile: "/user/profile",
    connections: "/user/connections",
    security: "/user/security",
    invitations: "/user/invitations",
    danger: "/user/danger",
  });

  const typedRoutes: readonly Route[] = Object.values(accountRoutes);
  expect(typedRoutes).toHaveLength(6);
});

it("redirects the account root to profile", () => {
  expect(() => UserPage()).toThrow("NEXT_REDIRECT:/user/profile");
});
