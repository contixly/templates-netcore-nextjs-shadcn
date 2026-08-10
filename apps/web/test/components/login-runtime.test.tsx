import { render, screen } from "@testing-library/react";

import { LoginRuntime } from "@/src/features/authentication/ui/login-runtime";
import { loadServerAuthState } from "@/src/lib/api/auth/server/load-server-auth-state";
import { renderWithMessages } from "@/test/support/render";

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
      eyebrow: "Authentication",
      title: "Sign in",
      description: "Use an available sign-in method.",
      unavailable: "No production sign-in provider is configured yet.",
      "failure.title": "Authentication is unavailable",
      "failure.description": "Try again later.",
    })[key] ?? key,
}));
jest.mock("@/src/lib/api/auth/server/load-server-auth-state", () => ({
  loadServerAuthState: jest.fn(),
}));
jest.mock("@/src/features/authentication/ui/auth-api-failure", () => ({
  AuthApiFailure: ({ failure }: { failure: { traceId?: string } }) => (
    <section role="alert">
      <h2>Authentication is unavailable</h2>
      <p>Try again later.</p>
      {failure.traceId ? <p>{failure.traceId}</p> : null}
    </section>
  ),
}));
jest.mock(
  "@/src/features/authentication/ui/local-automation-login-panel",
  () => ({
    LocalAutomationLoginPanel: ({ redirectPath }: { redirectPath: string }) => (
      <div data-testid="local-panel">{redirectPath}</div>
    ),
  }),
);

const loadState = jest.mocked(loadServerAuthState);

it("shows configured providers alongside gated local automation", async () => {
  loadState.mockResolvedValue({
    ok: true,
    data: {
      capabilities: {
        localAutomationEnabled: true,
        providers: [{ id: "google", displayName: "Google" }],
      },
      session: { authenticated: false, user: null, session: null },
    },
  });

  renderWithMessages(
    await LoginRuntime({
      searchParams: Promise.resolve({ redirect: "/dashboard" }),
    }),
  );

  expect(screen.getByTestId("local-panel")).toHaveTextContent("/dashboard");
  expect(
    screen.getByRole("button", { name: "Continue with Google" }),
  ).toBeEnabled();
  expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
});

it("shows the deferred-provider state when local automation is disabled", async () => {
  loadState.mockResolvedValue({
    ok: true,
    data: {
      capabilities: { localAutomationEnabled: false, providers: [] },
      session: { authenticated: false, user: null, session: null },
    },
  });

  render(
    await LoginRuntime({
      searchParams: Promise.resolve({ redirect: "/dashboard" }),
    }),
  );

  expect(
    screen.getByText("No production sign-in provider is configured yet."),
  ).toBeInTheDocument();
  expect(screen.queryByTestId("local-panel")).not.toBeInTheDocument();
});

it("shows configured providers without the local automation panel", async () => {
  loadState.mockResolvedValue({
    ok: true,
    data: {
      capabilities: {
        localAutomationEnabled: false,
        providers: [{ id: "github", displayName: "GitHub" }],
      },
      session: { authenticated: false, user: null, session: null },
    },
  });

  renderWithMessages(
    await LoginRuntime({
      searchParams: Promise.resolve({ redirect: "/settings?tab=profile" }),
    }),
  );

  expect(
    screen.getByRole("button", { name: "Continue with GitHub" }),
  ).toBeEnabled();
  expect(
    screen.queryByText("No production sign-in provider is configured yet."),
  ).not.toBeInTheDocument();
  expect(screen.queryByTestId("local-panel")).not.toBeInTheDocument();
});

it("redirects an authenticated session to the sanitized target", async () => {
  loadState.mockResolvedValue({
    ok: true,
    data: {
      capabilities: { localAutomationEnabled: true, providers: [] },
      session: {
        authenticated: true,
        user: {
          id: "01900000-0000-7000-8000-000000000001",
          name: "Local User",
          email: "local-agent+runtime@local-agent.test",
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
    },
  });

  await expect(
    LoginRuntime({
      searchParams: Promise.resolve({ redirect: "https://evil.test" }),
    }),
  ).rejects.toThrow("NEXT_REDIRECT:/dashboard");
});

it("renders a safe failure instead of local controls", async () => {
  loadState.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "internal_error",
      status: 500,
      traceId: "trace-login",
    },
  });

  render(
    await LoginRuntime({
      searchParams: Promise.resolve({ redirect: "/dashboard" }),
    }),
  );

  expect(
    screen.getByRole("heading", { name: "Authentication is unavailable" }),
  ).toBeInTheDocument();
  expect(screen.getByText("trace-login")).toBeInTheDocument();
  expect(screen.queryByTestId("local-panel")).not.toBeInTheDocument();
});
