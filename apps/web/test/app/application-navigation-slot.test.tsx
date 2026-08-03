import { render, screen } from "@testing-library/react";

import { ApplicationNavigationSlot } from "@/src/components/application/application-navigation-slot";
import { loadApplicationShell } from "@/src/lib/api/application/server/load-application-shell";

jest.mock("next/server", () => ({
  connection: jest.fn().mockResolvedValue(undefined),
}));
jest.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) =>
    ({
      errorTitle: "Something went wrong",
      errorDescription:
        "The application could not be loaded safely. Try again.",
    })[key] ?? key,
}));
jest.mock("@/src/components/authentication/browser-session-refresh", () => ({
  BrowserSessionRefresh: () => <i data-testid="browser-session-refresh" />,
}));
jest.mock("@/src/components/application/application-sidebar", () => ({
  ApplicationSidebar: () => (
    <nav aria-label="Workspace" data-slot="application-navigation" />
  ),
}));
jest.mock("@/src/lib/api/application/server/load-application-shell", () => ({
  loadApplicationShell: jest.fn(),
}));

const mockLoadApplicationShell = jest.mocked(loadApplicationShell);

beforeEach(() => {
  jest.clearAllMocks();
});

it("renders one renewal owner and one semantic navigation on success", async () => {
  mockLoadApplicationShell.mockResolvedValue({
    ok: true,
    data: {
      account: {
        id: "account-id",
        displayName: "Account User",
        primaryEmail: "account@example.test",
        imageUrl: null,
        createdAt: "2026-08-03T10:00:00Z",
        verifiedEmails: [],
      },
      organizations: [],
      nextOrganizationCursor: null,
      session: {
        id: "session-id",
        createdAt: "2026-08-03T10:00:00Z",
        updatedAt: "2026-08-03T10:00:00Z",
        expiresAt: "2026-08-04T10:00:00Z",
        activeOrganizationId: null,
      },
      user: {
        id: "user-id",
        name: "User",
        email: "user@example.test",
        emailVerified: true,
        image: null,
      },
      currentOrganization: null,
    },
  });

  render(await ApplicationNavigationSlot({ redirectPath: "/user/security" }));

  expect(screen.getAllByTestId("browser-session-refresh")).toHaveLength(1);
  expect(screen.getAllByRole("navigation")).toHaveLength(1);
  expect(screen.getByRole("navigation")).toHaveAttribute(
    "data-slot",
    "application-navigation",
  );
  expect(mockLoadApplicationShell).toHaveBeenCalledWith(
    "/user/security",
    undefined,
  );
});

it("renders localized safe failure copy and only the optional trace identifier", async () => {
  mockLoadApplicationShell.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "private_backend_failure",
      status: 503,
      traceId: "trace-shell-safe",
    },
  });

  render(
    await ApplicationNavigationSlot({
      redirectPath: "/w/acme/dashboard",
      organizationKey: "acme",
    }),
  );

  const alert = screen.getByRole("alert");
  expect(alert).toHaveTextContent("Something went wrong");
  expect(alert).toHaveTextContent(
    "The application could not be loaded safely. Try again.",
  );
  expect(alert).toHaveTextContent("trace-shell-safe");
  expect(alert).not.toHaveTextContent("private_backend_failure");
  expect(
    screen.queryByTestId("browser-session-refresh"),
  ).not.toBeInTheDocument();
  expect(screen.queryByRole("navigation")).not.toBeInTheDocument();
  expect(mockLoadApplicationShell).toHaveBeenCalledWith(
    "/w/acme/dashboard",
    "acme",
  );
});
