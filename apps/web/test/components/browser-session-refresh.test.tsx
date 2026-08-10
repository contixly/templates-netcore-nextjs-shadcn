import { render, waitFor } from "@testing-library/react";

import { BrowserSessionRefresh } from "@/src/features/authentication/ui/browser-session-refresh";
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
  delete (
    document as unknown as Document &
      Record<symbol, Readonly<{ pathname: string }> | undefined>
  )[refreshStartedMarker];
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

it("defers a dashboard resolver navigation to its final destination cycle", async () => {
  pathname = "/w/acme/dashboard";
  const { rerender } = render(<BrowserSessionRefresh />);
  await waitFor(() => {
    expect(refreshSession).toHaveBeenCalledTimes(1);
    expect(refreshRoute).toHaveBeenCalledTimes(1);
  });

  pathname = "/dashboard";
  rerender(<BrowserSessionRefresh />);
  await Promise.resolve();
  expect(refreshSession).toHaveBeenCalledTimes(1);

  pathname = "/w/acme/dashboard";
  rerender(<BrowserSessionRefresh />);
  await waitFor(() => {
    expect(refreshSession).toHaveBeenCalledTimes(2);
    expect(refreshRoute).toHaveBeenCalledTimes(2);
  });
});

it("renews each protected pathname during same-document soft navigation", async () => {
  const { rerender } = render(<BrowserSessionRefresh />);

  await waitFor(() => {
    expect(refreshSession).toHaveBeenCalledTimes(1);
    expect(refreshRoute).toHaveBeenCalledTimes(1);
  });

  pathname = "/w/acme/settings/workspace";
  rerender(<BrowserSessionRefresh />);
  await waitFor(() => {
    expect(refreshSession).toHaveBeenCalledTimes(2);
    expect(refreshRoute).toHaveBeenCalledTimes(2);
  });

  pathname = "/user/profile";
  rerender(<BrowserSessionRefresh />);
  await waitFor(() => {
    expect(refreshSession).toHaveBeenCalledTimes(3);
    expect(refreshRoute).toHaveBeenCalledTimes(3);
  });

  pathname = "/user/security";
  rerender(<BrowserSessionRefresh />);
  await waitFor(() => {
    expect(refreshSession).toHaveBeenCalledTimes(4);
    expect(refreshRoute).toHaveBeenCalledTimes(4);
  });
});

it("deduplicates the success refresh and same-path remount", async () => {
  const first = render(<BrowserSessionRefresh />);

  await waitFor(() => {
    expect(refreshSession).toHaveBeenCalledTimes(1);
    expect(refreshRoute).toHaveBeenCalledTimes(1);
  });

  first.rerender(<BrowserSessionRefresh />);
  first.unmount();
  render(<BrowserSessionRefresh />);
  await Promise.resolve();
  expect(refreshSession).toHaveBeenCalledTimes(1);
  expect(refreshRoute).toHaveBeenCalledTimes(1);
});

it("deduplicates concurrent mounts for the same protected pathname", async () => {
  render(
    <>
      <BrowserSessionRefresh />
      <BrowserSessionRefresh />
    </>,
  );

  await waitFor(() => {
    expect(refreshSession).toHaveBeenCalledTimes(1);
    expect(refreshRoute).toHaveBeenCalledTimes(1);
  });
});

it("releases a failed pathname cycle so a later remount can retry", async () => {
  refreshSession.mockResolvedValueOnce({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });

  const first = render(<BrowserSessionRefresh />);

  await waitFor(() => expect(refreshSession).toHaveBeenCalledTimes(1));
  expect(refreshRoute).not.toHaveBeenCalled();

  first.unmount();
  render(<BrowserSessionRefresh />);

  await waitFor(() => {
    expect(refreshSession).toHaveBeenCalledTimes(2);
    expect(refreshRoute).toHaveBeenCalledTimes(1);
  });
});
