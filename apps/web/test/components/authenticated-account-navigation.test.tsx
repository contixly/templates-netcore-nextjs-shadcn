import { connection } from "next/server";

import { AccountHeaderNavigation } from "@/src/components/account/account-header-navigation";
import { AuthenticatedAccountNavigation } from "@/src/components/account/authenticated-account-navigation";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";

jest.mock("next/server", () => ({
  connection: jest.fn().mockResolvedValue(undefined),
}));
jest.mock("@/src/lib/api/auth/server/load-server-auth-session", () => ({
  loadServerAuthSession: jest.fn(),
}));

const loadSession = jest.mocked(loadServerAuthSession);

beforeEach(() => {
  jest.clearAllMocks();
});

it("renders the account navigation only for a confirmed authenticated session", async () => {
  loadSession.mockResolvedValue({
    ok: true,
    data: {
      authenticated: true,
      user: {
        id: "user-id",
        name: "Workspace User",
        email: "workspace@example.test",
        emailVerified: true,
        image: null,
      },
      session: {
        id: "session-id",
        createdAt: "2026-07-30T10:00:00Z",
        updatedAt: "2026-07-30T10:00:00Z",
        expiresAt: "2026-08-01T10:00:00Z",
        activeOrganizationId: "organization-id",
      },
    },
  });

  const navigation = await AuthenticatedAccountNavigation();

  expect(connection).toHaveBeenCalledTimes(1);
  expect(loadSession).toHaveBeenCalledTimes(1);
  expect(navigation).toEqual(<AccountHeaderNavigation />);
});

it.each([
  {
    ok: true as const,
    data: {
      authenticated: false as const,
      user: null,
      session: null,
    },
  },
  {
    ok: false as const,
    failure: {
      kind: "network" as const,
      code: "api_unavailable" as const,
    },
  },
])(
  "hides account navigation when authentication is not confirmed",
  async (result) => {
    loadSession.mockResolvedValue(result);

    await expect(AuthenticatedAccountNavigation()).resolves.toBeNull();
  },
);
