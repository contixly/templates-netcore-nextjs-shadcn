import { fireEvent, render, screen } from "@testing-library/react";

import ConnectionsError from "@/src/app/(protected)/user/connections/error";
import ConnectionsLoading from "@/src/app/(protected)/user/connections/loading";
import ConnectionsPage from "@/src/app/(protected)/user/connections/page";
import DangerError from "@/src/app/(protected)/user/danger/error";
import DangerLoading from "@/src/app/(protected)/user/danger/loading";
import DangerPage from "@/src/app/(protected)/user/danger/page";
import ProfileError from "@/src/app/(protected)/user/profile/error";
import ProfileLoading from "@/src/app/(protected)/user/profile/loading";
import ProfilePage from "@/src/app/(protected)/user/profile/page";
import SecurityError from "@/src/app/(protected)/user/security/error";
import SecurityLoading from "@/src/app/(protected)/user/security/loading";
import SecurityPage from "@/src/app/(protected)/user/security/page";
import { loadAccount } from "@/src/lib/api/account/server/load-account";
import { loadConnections } from "@/src/lib/api/account/server/load-connections";
import { loadSessions } from "@/src/lib/api/account/server/load-sessions";
import type {
  AccountConnectionsResponse,
  AccountResponse,
  AccountSessionsResponse,
} from "@/src/lib/api/generated";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/account/server/load-account", () => ({
  loadAccount: jest.fn(),
}));
jest.mock("@/src/lib/api/account/server/load-connections", () => ({
  loadConnections: jest.fn(),
}));
jest.mock("@/src/lib/api/account/server/load-sessions", () => ({
  loadSessions: jest.fn(),
}));
jest.mock("@/src/components/account/profile-form", () => ({
  ProfileForm: ({ initialAccount }: { initialAccount: AccountResponse }) => (
    <p>
      profile projection {initialAccount.id} {initialAccount.primaryEmail}
    </p>
  ),
}));
jest.mock("@/src/components/account/connections-list", () => ({
  ConnectionsList: ({
    initialConnections,
  }: {
    initialConnections: AccountConnectionsResponse;
  }) => <p>connections projection {initialConnections.items.length}</p>,
}));
jest.mock("@/src/components/account/session-list", () => ({
  SessionList: ({ initialPage }: { initialPage: AccountSessionsResponse }) => (
    <p>sessions projection {initialPage.items.length}</p>
  ),
}));
jest.mock("@/src/components/account/delete-account-dialog", () => ({
  DeleteAccountDialog: ({ primaryEmail }: { primaryEmail: string }) => (
    <p>delete projection {primaryEmail}</p>
  ),
}));
jest.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => {
    const messages: Record<string, string> = {
      "account.pages.profile.title": "Profile settings",
      "account.pages.profile.description": "Manage your account profile.",
      "account.pages.profile.sectionTitle": "Profile details",
      "account.pages.profile.loading": "Loading profile",
      "account.pages.connections.title": "Connections",
      "account.pages.connections.description": "Manage sign-in providers.",
      "account.pages.connections.sectionTitle": "Sign-in connections",
      "account.pages.connections.loading": "Loading connections",
      "account.pages.security.title": "Security",
      "account.pages.security.description": "Manage active sessions.",
      "account.pages.security.sectionTitle": "Active sessions",
      "account.pages.security.loading": "Loading security",
      "account.pages.danger.title": "Danger zone",
      "account.pages.danger.description": "Manage irreversible actions.",
      "account.pages.danger.loading": "Loading danger zone",
      "account.danger.title": "Delete account",
      "account.danger.description": "Permanently delete your account.",
      "account.danger.warning": "This action cannot be undone.",
      "account.failure.title": "Account settings are unavailable",
      "account.failure.description": "Try again without exposing private data.",
    };

    return (key: string) => messages[`${namespace}.${key}`] ?? key;
  },
}));

const account = {
  id: "01900000-0000-7000-8000-000000000001",
  displayName: "Account User",
  primaryEmail: "account@example.test",
  imageUrl: null,
  createdAt: "2026-07-28T09:30:00Z",
  verifiedEmails: [
    {
      email: "account@example.test",
      isPrimary: true,
      providers: ["google"],
    },
  ],
} satisfies AccountResponse;

const connections = {
  items: [],
} satisfies AccountConnectionsResponse;

const sessions = {
  items: [],
  nextCursor: null,
} satisfies AccountSessionsResponse;

const loadAccountMock = jest.mocked(loadAccount);
const loadConnectionsMock = jest.mocked(loadConnections);
const loadSessionsMock = jest.mocked(loadSessions);

beforeEach(() => {
  jest.clearAllMocks();
});

it("loads each projection through its Task 12 server adapter", async () => {
  loadAccountMock.mockResolvedValue({ ok: true, data: account });
  loadConnectionsMock.mockResolvedValue({ ok: true, data: connections });
  loadSessionsMock.mockResolvedValue({ ok: true, data: sessions });

  let view = render(await ProfilePage());
  expect(screen.getByText(/profile projection/)).toHaveTextContent(account.id);
  expect(
    screen.getByRole("heading", { level: 1, name: "Profile settings" }),
  ).toBeVisible();
  expect(
    screen
      .getByText(/profile projection/)
      .closest('[data-slot="settings-page-section"]'),
  ).toHaveClass("max-w-3xl");
  expect(
    Array.from(view.container.querySelectorAll("h1, h2"), (heading) =>
      heading.textContent?.trim(),
    ),
  ).toEqual(["Profile settings", "Profile details"]);
  expect(loadAccountMock).toHaveBeenCalledTimes(1);
  view.unmount();

  view = render(await ConnectionsPage());
  expect(screen.getByText("connections projection 0")).toBeInTheDocument();
  expect(
    screen
      .getByText("connections projection 0")
      .closest('[data-slot="settings-page-section"]'),
  ).toHaveAttribute("data-mode", "wide");
  expect(
    Array.from(view.container.querySelectorAll("h1, h2"), (heading) =>
      heading.textContent?.trim(),
    ),
  ).toEqual(["Connections", "Sign-in connections"]);
  expect(loadConnectionsMock).toHaveBeenCalledTimes(1);
  view.unmount();

  view = render(await SecurityPage());
  expect(screen.getByText("sessions projection 0")).toBeInTheDocument();
  expect(
    screen
      .getByText("sessions projection 0")
      .closest('[data-slot="settings-page-section"]'),
  ).toHaveClass("max-w-3xl");
  expect(
    Array.from(view.container.querySelectorAll("h1, h2"), (heading) =>
      heading.textContent?.trim(),
    ),
  ).toEqual(["Security", "Active sessions"]);
  expect(loadSessionsMock).toHaveBeenCalledWith();
  view.unmount();

  view = render(await DangerPage());
  expect(
    screen.getByText("delete projection account@example.test"),
  ).toBeInTheDocument();
  expect(
    screen.getByRole("region", { name: "Delete account" }),
  ).toHaveAttribute("data-variant", "destructive");
  expect(
    Array.from(view.container.querySelectorAll("h1, h2"), (heading) =>
      heading.textContent?.trim(),
    ),
  ).toEqual(["Danger zone", "Delete account"]);
  expect(loadAccountMock).toHaveBeenCalledTimes(2);
});

it.each([
  ["profile", ProfilePage, loadAccountMock],
  ["connections", ConnectionsPage, loadConnectionsMock],
  ["security", SecurityPage, loadSessionsMock],
] as const)("renders a safe %s projection failure", async (_, Page, load) => {
  load.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "internal_error",
      status: 500,
      traceId: "trace-safe",
    },
  });

  render(await Page());

  expect(screen.getByRole("alert")).toHaveTextContent(
    "Account settings are unavailable",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-safe");
  expect(screen.queryByText("internal_error")).not.toBeInTheDocument();
});

it("renders all four localized loading states", async () => {
  for (const [Loading, label] of [
    [ProfileLoading, "Loading profile"],
    [ConnectionsLoading, "Loading connections"],
    [SecurityLoading, "Loading security"],
    [DangerLoading, "Loading danger zone"],
  ] as const) {
    const { unmount } = render(await Loading());
    expect(screen.getByRole("status")).toHaveTextContent(label);
    unmount();
  }
});

it("renders all four safe error boundaries and retries", () => {
  for (const Boundary of [
    ProfileError,
    ConnectionsError,
    SecurityError,
    DangerError,
  ]) {
    const reset = jest.fn();
    const { unmount } = renderWithMessages(
      <Boundary error={new Error("private-account-error")} reset={reset} />,
    );

    expect(screen.queryByText("private-account-error")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(reset).toHaveBeenCalledTimes(1);
    unmount();
  }
});
