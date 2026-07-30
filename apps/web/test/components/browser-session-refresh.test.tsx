import { render, waitFor } from "@testing-library/react";

import { BrowserSessionRefresh } from "@/src/components/authentication/browser-session-refresh";
import { refreshBrowserAuthSession } from "@/src/lib/api/auth/browser/refresh-browser-auth-session";

const refreshRoute = jest.fn();
let pathname = "/workspaces";
const refreshStartedMarker = Symbol.for(
  "template.browser-session-refresh.started",
);

jest.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: refreshRoute }),
  usePathname: () => pathname,
}));
jest.mock("@/src/lib/api/auth/browser/refresh-browser-auth-session", () => ({
  refreshBrowserAuthSession: jest.fn(),
}));

const refreshSession = jest.mocked(refreshBrowserAuthSession);

beforeEach(() => {
  jest.clearAllMocks();
  pathname = "/workspaces";
  delete (window as unknown as Window & Record<symbol, boolean | undefined>)[
    refreshStartedMarker
  ];
  refreshSession.mockResolvedValue({
    ok: true,
    data: {
      authenticated: true,
      user: {
        id: "01900000-0000-7000-8000-000000000001",
        name: "Local User",
        email: "local-agent+refresh@local-agent.test",
        emailVerified: false,
        image: null,
      },
      session: {
        id: "01900000-0000-7000-8000-000000000002",
        createdAt: "2026-07-24T00:00:00Z",
        updatedAt: "2026-07-24T00:00:00Z",
        expiresAt: "2026-07-31T00:00:00Z",
        activeOrganizationId: null,
      },
    },
  });
});

it("waits for the dashboard resolver to reach its protected destination", async () => {
  pathname = "/dashboard";

  render(<BrowserSessionRefresh />);

  await Promise.resolve();
  expect(refreshSession).not.toHaveBeenCalled();
  expect(refreshRoute).not.toHaveBeenCalled();
});

it("refreshes the authenticated browser session once per document", async () => {
  const { container, rerender, unmount } = render(<BrowserSessionRefresh />);

  expect(container).toBeEmptyDOMElement();
  await waitFor(() => {
    expect(refreshSession).toHaveBeenCalledTimes(1);
    expect(refreshRoute).toHaveBeenCalledTimes(1);
  });

  rerender(<BrowserSessionRefresh />);
  await Promise.resolve();
  expect(refreshSession).toHaveBeenCalledTimes(1);
  expect(refreshRoute).toHaveBeenCalledTimes(1);

  unmount();
  render(<BrowserSessionRefresh />);
  await Promise.resolve();
  expect(refreshSession).toHaveBeenCalledTimes(1);
  expect(refreshRoute).toHaveBeenCalledTimes(1);
});

it("does not refresh the dashboard when the browser session read fails", async () => {
  refreshSession.mockResolvedValueOnce({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });

  render(<BrowserSessionRefresh />);

  await waitFor(() => expect(refreshSession).toHaveBeenCalledTimes(1));
  expect(refreshRoute).not.toHaveBeenCalled();
});
