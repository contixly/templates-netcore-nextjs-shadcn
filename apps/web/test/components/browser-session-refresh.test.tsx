import { render, waitFor } from "@testing-library/react";

import { BrowserSessionRefresh } from "@/src/components/authentication/browser-session-refresh";
import { refreshBrowserAuthSession } from "@/src/lib/api/auth/browser/refresh-browser-auth-session";

jest.mock("@/src/lib/api/auth/browser/refresh-browser-auth-session", () => ({
  refreshBrowserAuthSession: jest.fn(),
}));

const refreshSession = jest.mocked(refreshBrowserAuthSession);

beforeEach(() => {
  jest.clearAllMocks();
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
      },
    },
  });
});

it("refreshes the authenticated browser session after hydration", async () => {
  const { container } = render(<BrowserSessionRefresh />);

  expect(container).toBeEmptyDOMElement();
  await waitFor(() => expect(refreshSession).toHaveBeenCalledTimes(1));
});
