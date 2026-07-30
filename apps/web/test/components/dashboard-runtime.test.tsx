import { render, screen } from "@testing-library/react";

import { DashboardRuntime } from "@/src/components/authentication/dashboard-runtime";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";

jest.mock("next/server", () => ({
  connection: jest.fn().mockResolvedValue(undefined),
}));
jest.mock("next/navigation", () => ({
  redirect: jest.fn((path: string) => {
    throw new Error(`NEXT_REDIRECT:${path}`);
  }),
}));
jest.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) =>
    ({
      eyebrow: "Iteration 3 session proof",
      title: "Authenticated dashboard",
      description: "This temporary page proves the browser session.",
      name: "Name",
      email: "Email",
      emailVerified: "Email verified",
      sessionId: "Session ID",
      expiresAt: "Expires",
      yes: "Yes",
      no: "No",
      "failure.title": "Authentication is unavailable",
      "failure.description": "Try again later.",
    })[key] ?? key,
}));
jest.mock("@/src/lib/api/auth/server/load-server-auth-session", () => ({
  loadServerAuthSession: jest.fn(),
}));
jest.mock("@/src/components/authentication/auth-api-failure", () => ({
  AuthApiFailure: () => (
    <section role="alert">
      <h2>Authentication is unavailable</h2>
      <p>Try again later.</p>
    </section>
  ),
}));
jest.mock("@/src/components/authentication/logout-button", () => ({
  LogoutButton: () => <button type="button">Log out</button>,
}));
jest.mock("@/src/components/authentication/browser-session-refresh", () => ({
  BrowserSessionRefresh: () => <i data-testid="browser-session-refresh" />,
}));
const loadSession = jest.mocked(loadServerAuthSession);
const redirect = jest.mocked(jest.requireMock("next/navigation").redirect);

beforeEach(() => {
  jest.clearAllMocks();
});

it("redirects only an explicit anonymous projection", async () => {
  loadSession.mockResolvedValue({
    ok: true,
    data: { authenticated: false, user: null, session: null },
  });

  await expect(DashboardRuntime()).rejects.toThrow(
    "NEXT_REDIRECT:/auth/login?redirect=%2Fdashboard",
  );
});

it("renders a safe failure instead of redirecting on API outage", async () => {
  loadSession.mockResolvedValue({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });

  render(await DashboardRuntime());

  expect(
    screen.getByRole("heading", { name: "Authentication is unavailable" }),
  ).toBeInTheDocument();
  expect(redirect).not.toHaveBeenCalled();
});

it("fails closed instead of redirecting when authenticated is missing", async () => {
  const malformedResult = {
    ok: true,
    data: { user: null, session: null },
  } as unknown as Awaited<ReturnType<typeof loadServerAuthSession>>;
  loadSession.mockResolvedValue(malformedResult);

  render(await DashboardRuntime());

  expect(
    screen.getByRole("heading", { name: "Authentication is unavailable" }),
  ).toBeInTheDocument();
  expect(redirect).not.toHaveBeenCalled();
});

it("renders only safe user and session fields", async () => {
  loadSession.mockResolvedValue({
    ok: true,
    data: {
      authenticated: true,
      user: {
        id: "01900000-0000-7000-8000-000000000001",
        name: "Local Dashboard User",
        email: "local-agent+dashboard@local-agent.test",
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

  render(await DashboardRuntime());

  expect(
    screen.getByRole("heading", { name: "Authenticated dashboard" }),
  ).toBeInTheDocument();
  expect(screen.getByText("Local Dashboard User")).toBeInTheDocument();
  expect(
    screen.getByText("local-agent+dashboard@local-agent.test"),
  ).toBeInTheDocument();
  expect(
    screen.getByText("01900000-0000-7000-8000-000000000002"),
  ).toBeInTheDocument();
  expect(
    screen.queryByTestId("browser-session-refresh"),
  ).not.toBeInTheDocument();
  expect(document.body.textContent).not.toMatch(/password|ticket_key|cookie/i);
});
