import { fireEvent, screen, waitFor, within } from "@testing-library/react";

import { SessionList } from "@/src/components/account/session-list";
import {
  revokeBrowserAccountSession,
  revokeOtherBrowserAccountSessions,
} from "@/src/lib/api/account/browser/account-mutations";
import {
  getAccountSessions,
  type AccountSessionResponse,
  type AccountSessionsResponse,
} from "@/src/lib/api/generated";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/account/browser/account-mutations", () => ({
  revokeBrowserAccountSession: jest.fn(),
  revokeOtherBrowserAccountSessions: jest.fn(),
}));
jest.mock("@/src/lib/api/generated", () => ({
  getAccountSessions: jest.fn(),
}));

const currentSession = {
  id: "01900000-0000-7000-8000-000000000011",
  createdAt: "2026-07-27T09:00:00Z",
  lastSeenAt: "2026-07-29T09:00:00Z",
  expiresAt: "2026-08-05T09:00:00Z",
  isCurrent: true,
  authenticationMethod: "google",
  ipAddress: "203.0.113.0/24",
  userAgent:
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 Chrome/138.0.0.0 Safari/537.36",
} satisfies AccountSessionResponse;

const otherSession = {
  id: "01900000-0000-7000-8000-000000000012",
  createdAt: "2026-07-26T09:00:00Z",
  lastSeenAt: "2026-07-28T09:00:00Z",
  expiresAt: "2026-08-04T09:00:00Z",
  isCurrent: false,
  authenticationMethod: "github",
  ipAddress: null,
  userAgent:
    "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1",
} satisfies AccountSessionResponse;

const initialPage = {
  items: [currentSession, otherSession],
  nextCursor: "cursor-next",
} satisfies AccountSessionsResponse;

const getSessions = jest.mocked(getAccountSessions);
const revokeSession = jest.mocked(revokeBrowserAccountSession);
const revokeOthers = jest.mocked(revokeOtherBrowserAccountSessions);

beforeEach(() => {
  jest.clearAllMocks();
});

it("displays safe session details without exposing the raw user agent", () => {
  renderWithMessages(<SessionList initialPage={initialPage} />);

  const current = screen.getByRole("article", {
    name: "Chrome on macOS, Current session",
  });
  expect(current).toHaveTextContent("Current session");
  expect(current).toHaveTextContent("Signed in with Google");
  expect(current).toHaveTextContent("203.0.113.0/24");
  expect(current).toHaveTextContent("Last active Jul 29, 2026");
  expect(current).not.toHaveTextContent(currentSession.userAgent);

  const mobile = screen.getByRole("article", {
    name: "Safari on iOS",
  });
  expect(mobile).toHaveTextContent("Signed in with GitHub");
});

it("does not offer revoke for the current session", () => {
  renderWithMessages(<SessionList initialPage={initialPage} />);

  const current = screen.getByRole("article", {
    name: "Chrome on macOS, Current session",
  });
  expect(
    within(current).queryByRole("button", { name: "Revoke session" }),
  ).not.toBeInTheDocument();
  expect(
    within(screen.getByRole("article", { name: "Safari on iOS" })).getByRole(
      "button",
      { name: "Revoke session" },
    ),
  ).toBeEnabled();
});

it("loads the next cursor page, appends new sessions, and deduplicates ids", async () => {
  const newSession = {
    ...otherSession,
    id: "01900000-0000-7000-8000-000000000013",
    userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Edg/138.0",
  } satisfies AccountSessionResponse;
  getSessions.mockResolvedValue({
    data: {
      data: {
        items: [otherSession, newSession],
        nextCursor: null,
      },
    },
  } as Awaited<ReturnType<typeof getAccountSessions>>);
  renderWithMessages(<SessionList initialPage={initialPage} />);

  fireEvent.click(screen.getByRole("button", { name: "Load more sessions" }));

  await waitFor(() => {
    expect(getSessions).toHaveBeenCalledWith({
      client: { id: "browser-client" },
      cache: "no-store",
      query: { cursor: "cursor-next" },
    });
  });
  expect(screen.getAllByRole("article")).toHaveLength(3);
  expect(
    screen.getByRole("article", { name: "Edge on Windows" }),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Load more sessions" }),
  ).not.toBeInTheDocument();
});

it("removes a session only after a successful revoke", async () => {
  revokeSession.mockResolvedValue({
    ok: true,
    data: { sessionId: otherSession.id },
  });
  renderWithMessages(
    <SessionList initialPage={{ ...initialPage, nextCursor: null }} />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Revoke session" }));

  expect(await screen.findByRole("status")).toHaveTextContent(
    "Session revoked.",
  );
  expect(revokeSession).toHaveBeenCalledWith(
    { id: "browser-client" },
    otherSession.id,
  );
  expect(
    screen.queryByRole("article", { name: "Safari on iOS" }),
  ).not.toBeInTheDocument();
});

it("revokes all other sessions while preserving the current session", async () => {
  revokeOthers.mockResolvedValue({
    ok: true,
    data: { revokedCount: 1 },
  });
  renderWithMessages(<SessionList initialPage={initialPage} />);

  fireEvent.click(
    screen.getByRole("button", { name: "Revoke all other sessions" }),
  );

  expect(await screen.findByRole("status")).toHaveTextContent(
    "1 other session revoked.",
  );
  expect(revokeOthers).toHaveBeenCalledWith({ id: "browser-client" });
  expect(screen.getAllByRole("article")).toHaveLength(1);
  expect(
    screen.getByRole("article", {
      name: "Chrome on macOS, Current session",
    }),
  ).toBeInTheDocument();
});

it("renders load and revoke failures without removing session state", async () => {
  getSessions.mockResolvedValue({
    error: {
      code: "invalid_cursor",
      traceId: "trace-cursor",
    },
    response: { status: 400 } as Response,
  } as Awaited<ReturnType<typeof getAccountSessions>>);
  revokeSession.mockResolvedValue({
    ok: false,
    failure: {
      kind: "network",
      code: "api_unavailable",
    },
  });
  renderWithMessages(<SessionList initialPage={initialPage} />);

  fireEvent.click(screen.getByRole("button", { name: "Load more sessions" }));
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Sessions could not be loaded.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-cursor");
  expect(
    screen.getByRole("button", { name: "Load more sessions" }),
  ).toBeEnabled();

  fireEvent.click(screen.getByRole("button", { name: "Revoke session" }));
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "The session could not be revoked.",
  );
  expect(
    screen.getByRole("article", { name: "Safari on iOS" }),
  ).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Revoke session" })).toBeEnabled();
});
