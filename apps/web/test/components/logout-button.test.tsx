import { fireEvent, screen, waitFor } from "@testing-library/react";

import { LogoutButton } from "@/src/components/authentication/logout-button";
import { logoutBrowserSession } from "@/src/lib/api/auth/browser/logout-browser-session";
import { renderWithMessages } from "@/test/support/render";

const replace = jest.fn();
const refresh = jest.fn();

jest.mock("next/navigation", () => ({
  useRouter: () => ({ replace, refresh }),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/auth/browser/logout-browser-session", () => ({
  logoutBrowserSession: jest.fn(),
}));

const logout = jest.mocked(logoutBrowserSession);

beforeEach(() => {
  jest.clearAllMocks();
});

it("logs out through REST, refreshes, and replaces dashboard history", async () => {
  logout.mockResolvedValue({
    ok: true,
    data: { authenticated: false, user: null, session: null },
  });
  renderWithMessages(<LogoutButton />);

  fireEvent.click(screen.getByRole("button", { name: "Log out" }));

  await waitFor(() => {
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(replace).toHaveBeenCalledWith("/auth/login");
  });
  expect(refresh.mock.invocationCallOrder[0]).toBeLessThan(
    replace.mock.invocationCallOrder[0],
  );
});

it("renders a localized safe failure without navigation", async () => {
  logout.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "antiforgery_failed",
      status: 400,
      traceId: "trace-logout",
    },
  });
  renderWithMessages(<LogoutButton />);

  fireEvent.click(screen.getByRole("button", { name: "Log out" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Could not log out safely.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-logout");
  expect(refresh).not.toHaveBeenCalled();
  expect(replace).not.toHaveBeenCalled();
});
