import { fireEvent, screen, waitFor } from "@testing-library/react";

import { LocalAutomationLoginPanel } from "@/src/components/authentication/local-automation-login-panel";
import { authenticationRoutes } from "@/src/features/authentication/authentication-routes";
import { createLocalAutomationBrowserSession } from "@/src/lib/api/auth/browser/create-local-automation-browser-session";
import { renderWithMessages } from "@/test/support/render";

const push = jest.fn();
const refresh = jest.fn();

jest.mock("next/navigation", () => ({
  useRouter: () => ({ push, refresh }),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock(
  "@/src/lib/api/auth/browser/create-local-automation-browser-session",
  () => ({ createLocalAutomationBrowserSession: jest.fn() }),
);

const createSession = jest.mocked(createLocalAutomationBrowserSession);

beforeEach(() => {
  jest.clearAllMocks();
});

it("creates a user, discards plaintext credentials, and navigates safely", async () => {
  createSession.mockResolvedValue({
    ok: true,
    data: {
      user: {
        id: "01900000-0000-7000-8000-000000000001",
        name: "Local User",
        email: "local-agent+panel@local-agent.test",
        emailVerified: false,
        image: null,
      },
      email: "local-agent+panel@local-agent.test",
      password: "local-must-never-render",
      cleanupUrl: "/api/local-auth/scenario",
    },
  });
  renderWithMessages(
    <LocalAutomationLoginPanel redirectPath={authenticationRoutes.dashboard} />,
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Create local automation user" }),
  );

  await waitFor(() => {
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(push).toHaveBeenCalledWith("/dashboard");
  });
  expect(screen.queryByText("local-must-never-render")).not.toBeInTheDocument();
});

it("localizes stable failures without backend detail", async () => {
  createSession.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "rate_limited",
      status: 429,
      traceId: "trace-panel",
    },
  });
  renderWithMessages(
    <LocalAutomationLoginPanel redirectPath={authenticationRoutes.dashboard} />,
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Create local automation user" }),
  );

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Too many local sign-in attempts.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-panel");
  expect(push).not.toHaveBeenCalled();
});
