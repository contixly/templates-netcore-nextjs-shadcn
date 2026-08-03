import { fireEvent, render, screen, waitFor } from "@testing-library/react";

import GlobalDashboardPage from "@/src/app/(protected)/dashboard/page";
import OrganizationDashboardError from "@/src/app/(protected)/w/[organizationKey]/dashboard/error";
import OrganizationDashboardLoading from "@/src/app/(protected)/w/[organizationKey]/dashboard/loading";
import OrganizationDashboardPage from "@/src/app/(protected)/w/[organizationKey]/dashboard/page";
import WorkspaceRootPage from "@/src/app/(protected)/w/[organizationKey]/page";
import WelcomePage from "@/src/app/(protected)/welcome/page";
import WorkspacesError from "@/src/app/(protected)/workspaces/error";
import WorkspacesLoading from "@/src/app/(protected)/workspaces/loading";
import WorkspacesPage from "@/src/app/(protected)/workspaces/page";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import { loadAccountInvitations } from "@/src/lib/api/collaboration/server/load-account-invitations";
import type {
  InvitationResponse,
  OrganizationDetailResponse,
  OrganizationSummaryResponse,
} from "@/src/lib/api/generated/types.gen";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";
import { renderWithMessages } from "@/test/support/render";

jest.mock("next/server", () => ({
  connection: jest.fn().mockResolvedValue(undefined),
}));
jest.mock("next/navigation", () => ({
  forbidden: jest.fn(() => {
    throw new Error("NEXT_FORBIDDEN");
  }),
  redirect: jest.fn((href: string) => {
    throw new Error(`NEXT_REDIRECT:${href}`);
  }),
  useRouter: () => ({ push: jest.fn(), refresh: jest.fn() }),
}));
jest.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => {
    const values: Record<string, string> = {
      "organizations.pages.workspaces.title": "Workspaces",
      "organizations.pages.workspaces.description":
        "Create and open your workspaces.",
      "organizations.pages.workspaces.loading": "Loading workspaces",
      "organizations.pages.dashboard.title": "Workspace dashboard",
      "organizations.pages.dashboard.description": "Current workspace context.",
      "organizations.pages.dashboard.loading": "Loading workspace dashboard",
      "organizations.pages.dashboard.name": "Workspace",
      "organizations.pages.dashboard.slug": "Slug",
      "organizations.failure.title": "Workspaces are unavailable",
      "organizations.failure.description": "Try again later.",
    };
    return (key: string) => values[`${namespace}.${key}`] ?? key;
  },
}));
jest.mock("@/src/lib/api/auth/server/load-server-auth-session", () => ({
  loadServerAuthSession: jest.fn(),
}));
jest.mock(
  "@/src/lib/api/collaboration/server/load-account-invitations",
  () => ({ loadAccountInvitations: jest.fn() }),
);
jest.mock("@/src/lib/api/organizations/server/load-organization", () => ({
  loadOrganization: jest.fn(),
}));
jest.mock("@/src/lib/api/organizations/server/load-organizations", () => ({
  loadOrganizations: jest.fn(),
}));
jest.mock("@/src/components/authentication/browser-session-refresh", () => ({
  BrowserSessionRefresh: () => <i data-testid="browser-session-refresh" />,
}));
jest.mock("@/src/components/application/site-header", () => ({
  OrganizationSwitcherRuntime: () => null,
}));

const redirect = jest.mocked(jest.requireMock("next/navigation").redirect);
const forbidden = jest.mocked(jest.requireMock("next/navigation").forbidden);
const loadSession = jest.mocked(loadServerAuthSession);
const loadDetail = jest.mocked(loadOrganization);
const loadList = jest.mocked(loadOrganizations);
const loadInvitations = jest.mocked(loadAccountInvitations);

const capabilities = {
  canUpdateOrganization: true,
  canDeleteOrganization: true,
  canAddMembers: true,
  canUpdateMemberRoles: true,
  canManageTeams: true,
  canManageInvitations: true,
  canManageApiKeys: true,
};
const acme = {
  id: "01900000-0000-7000-8000-000000000010",
  name: "Acme",
  slug: "acme",
  canonicalKey: "acme",
  createdAt: "2026-07-30T10:00:00Z",
  updatedAt: "2026-07-30T10:00:00Z",
  accessPrincipal: "user",
  currentRole: "owner",
  capabilities,
} satisfies OrganizationSummaryResponse;
const acmeDetail = {
  ...acme,
  allowedEmailDomains: [],
} satisfies OrganizationDetailResponse;
const invitation = {
  id: "01900000-0000-7000-8000-000000000101",
  organizationId: acme.id,
  organizationName: acme.name,
  canonicalOrganizationKey: acme.canonicalKey,
  teamId: null,
  teamName: null,
  email: "user@example.test",
  role: "member",
  status: "pending",
  displayState: "pending",
  expiresAt: "2026-08-03T12:00:00Z",
  createdAt: "2026-08-01T12:00:00Z",
  inviterId: "owner-id",
  inviterName: "Owner",
  invitationPath: "/invite/01900000-0000-7000-8000-000000000101",
} satisfies InvitationResponse;

function authenticated(activeOrganizationId: string | null) {
  loadSession.mockResolvedValue({
    ok: true,
    data: {
      authenticated: true,
      user: {
        id: "user-id",
        name: "User",
        email: "user@example.test",
        emailVerified: true,
        image: null,
      },
      session: {
        id: "session-id",
        createdAt: "2026-07-30T10:00:00Z",
        updatedAt: "2026-07-30T10:00:00Z",
        expiresAt: "2026-08-01T10:00:00Z",
        activeOrganizationId,
      },
    },
  });
}

function anonymous() {
  loadSession.mockResolvedValue({
    ok: true,
    data: { authenticated: false, user: null, session: null },
  });
}

beforeEach(() => {
  jest.clearAllMocks();
  authenticated(null);
  loadList.mockResolvedValue({
    ok: true,
    data: { items: [acme], nextCursor: null },
  });
  loadDetail.mockResolvedValue({ ok: true, data: acmeDetail });
  loadInvitations.mockResolvedValue({
    ok: true,
    data: { items: [], nextCursor: null },
  });
});

it("redirects existing-organization welcome through the dashboard", async () => {
  await expect(WelcomePage()).rejects.toThrow("NEXT_REDIRECT:/dashboard");
  expect(redirect).toHaveBeenCalledWith("/dashboard");
});

it.each([
  {
    label: "welcome",
    expected: "NEXT_REDIRECT:/auth/login?redirect=%2Fwelcome",
    renderPage: () => WelcomePage(),
  },
  {
    label: "workspaces",
    expected: "NEXT_REDIRECT:/auth/login?redirect=%2Fworkspaces",
    renderPage: () => WorkspacesPage({ searchParams: Promise.resolve({}) }),
  },
  {
    label: "workspace root",
    expected: "NEXT_REDIRECT:/auth/login?redirect=%2Fw%2Facme",
    renderPage: () =>
      WorkspaceRootPage({
        params: Promise.resolve({ organizationKey: "acme" }),
      }),
  },
  {
    label: "workspace dashboard",
    expected: "NEXT_REDIRECT:/auth/login?redirect=%2Fw%2Facme%2Fdashboard",
    renderPage: () =>
      OrganizationDashboardPage({
        params: Promise.resolve({ organizationKey: "acme" }),
      }),
  },
])(
  "redirects anonymous $label access to login",
  async ({ expected, renderPage }) => {
    anonymous();

    await expect(renderPage()).rejects.toThrow(expected);
  },
);

it("renders zero-organization onboarding on welcome", async () => {
  loadList.mockResolvedValue({
    ok: true,
    data: { items: [], nextCursor: null },
  });

  renderWithMessages(await WelcomePage());
  expect(
    screen.getByRole("heading", { name: "Create your first workspace" }),
  ).toBeVisible();
});

it("reuses the paged account invitation list in zero-workspace onboarding", async () => {
  loadList.mockResolvedValue({
    ok: true,
    data: { items: [], nextCursor: null },
  });
  loadInvitations.mockResolvedValue({
    ok: true,
    data: { items: [invitation], nextCursor: null },
  });

  renderWithMessages(await WelcomePage());
  expect(loadInvitations).toHaveBeenCalledWith({ limit: 20 });
  expect(screen.getByText("Acme")).toBeVisible();
  expect(
    screen.getByRole("link", { name: "Review invitation" }),
  ).toHaveAttribute("href", `/invite/${invitation.id}`);
});

it("routes zero-organization dashboard to welcome", async () => {
  loadList.mockResolvedValue({
    ok: true,
    data: { items: [], nextCursor: null },
  });

  await expect(GlobalDashboardPage()).rejects.toThrow("NEXT_REDIRECT:/welcome");
  expect(redirect).toHaveBeenCalledWith("/welcome");
});

it("prefers an accessible active organization for dashboard routing", async () => {
  authenticated("active-id");
  loadDetail.mockResolvedValue({
    ok: true,
    data: { ...acmeDetail, id: "active-id", canonicalKey: "active" },
  });

  await expect(GlobalDashboardPage()).rejects.toThrow(
    "NEXT_REDIRECT:/w/active/dashboard",
  );
  expect(loadDetail).toHaveBeenCalledWith("active-id");
  expect(redirect).toHaveBeenCalledWith("/w/active/dashboard");
});

it("starts active detail before the independent list settles and lets detail success win", async () => {
  authenticated("active-id");
  let settleList:
    | ((value: Awaited<ReturnType<typeof loadOrganizations>>) => void)
    | undefined;
  loadList.mockReturnValue(
    new Promise((resolve) => {
      settleList = resolve;
    }),
  );
  loadDetail.mockResolvedValue({
    ok: true,
    data: { ...acmeDetail, id: "active-id", canonicalKey: "active" },
  });

  const navigation = GlobalDashboardPage().catch((error: unknown) => error);
  await waitFor(() => {
    expect(loadDetail).toHaveBeenCalledWith("active-id");
  });
  await expect(navigation).resolves.toEqual(
    expect.objectContaining({
      message: "NEXT_REDIRECT:/w/active/dashboard",
    }),
  );
  settleList?.({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });
});

it("falls back to the first list item when the active organization is inaccessible", async () => {
  authenticated("stale-id");
  loadDetail.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "organization_not_found",
      status: 404,
    },
  });

  await expect(GlobalDashboardPage()).rejects.toThrow(
    "NEXT_REDIRECT:/w/acme/dashboard",
  );
  expect(redirect).toHaveBeenCalledWith("/w/acme/dashboard");
});

it("canonicalizes a UUID workspace root without mutating active context", async () => {
  const key = "01900000-0000-7000-8000-000000000010";

  await expect(
    WorkspaceRootPage({ params: Promise.resolve({ organizationKey: key }) }),
  ).rejects.toThrow("NEXT_REDIRECT:/w/acme/dashboard");
  expect(loadDetail).toHaveBeenCalledWith(key);
  expect(redirect).toHaveBeenCalledWith("/w/acme/dashboard");
});

it("lets workspace detail success win over an independent list failure", async () => {
  loadList.mockResolvedValue({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });

  await expect(
    WorkspaceRootPage({
      params: Promise.resolve({ organizationKey: "acme" }),
    }),
  ).rejects.toThrow("NEXT_REDIRECT:/w/acme/dashboard");
});

it("renders an accessible workspace dashboard despite list failure", async () => {
  loadList.mockResolvedValue({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });

  render(
    await OrganizationDashboardPage({
      params: Promise.resolve({ organizationKey: "acme" }),
    }),
  );

  expect(
    screen.getByRole("heading", { name: "Workspace dashboard" }),
  ).toBeVisible();
});

it.each(["01900000-0000-7000-8000-000000000010", "previous-acme-slug"])(
  "canonicalizes a noncanonical %s dashboard route",
  async (key) => {
    await expect(
      OrganizationDashboardPage({
        params: Promise.resolve({ organizationKey: key }),
      }),
    ).rejects.toThrow("NEXT_REDIRECT:/w/acme/dashboard");

    expect(loadDetail).toHaveBeenCalledWith(key);
    expect(redirect).toHaveBeenCalledWith("/w/acme/dashboard");
  },
);

it("calls forbidden for an unresolved key when organizations remain accessible", async () => {
  loadDetail.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "organization_not_found",
      status: 404,
    },
  });

  await expect(
    WorkspaceRootPage({
      params: Promise.resolve({ organizationKey: "missing" }),
    }),
  ).rejects.toThrow("NEXT_FORBIDDEN");
  expect(forbidden).toHaveBeenCalled();
});

it("renders onboarding from deep links when no organization is accessible", async () => {
  loadList.mockResolvedValue({
    ok: true,
    data: { items: [], nextCursor: null },
  });
  loadDetail.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "organization_not_found",
      status: 404,
    },
  });

  renderWithMessages(
    await OrganizationDashboardPage({
      params: Promise.resolve({ organizationKey: "missing" }),
    }),
  );
  expect(
    screen.getByRole("heading", { name: "Create your first workspace" }),
  ).toBeVisible();
});

it("renders only minimal accessible organization dashboard context", async () => {
  render(
    await OrganizationDashboardPage({
      params: Promise.resolve({ organizationKey: "acme" }),
    }),
  );

  expect(
    screen.getByRole("heading", { name: "Workspace dashboard" }),
  ).toBeVisible();
  expect(screen.getAllByText("Acme")).toHaveLength(2);
  expect(screen.getByText("acme")).toBeVisible();
  expect(
    screen.queryByTestId("browser-session-refresh"),
  ).not.toBeInTheDocument();
  expect(screen.queryByRole("table")).not.toBeInTheDocument();
  expect(redirect).not.toHaveBeenCalled();
});

it("renders only the authoritative first page at the canonical workspace URL", async () => {
  renderWithMessages(
    await WorkspacesPage({
      searchParams: Promise.resolve({}),
    }),
  );

  expect(loadList).toHaveBeenCalledTimes(1);
  expect(loadList).toHaveBeenCalledWith();
  expect(screen.getAllByRole("article")).toHaveLength(1);
});

it("redirects stale cursor bookmarks to the canonical first-page workspace URL", async () => {
  await expect(
    WorkspacesPage({
      searchParams: Promise.resolve({
        cursor: ["page-three", "amplified"],
      }),
    }),
  ).rejects.toThrow("NEXT_REDIRECT:/workspaces");

  expect(redirect).toHaveBeenCalledWith("/workspaces");
  expect(loadList).not.toHaveBeenCalled();
});

it("renders safe list failures with trace only", async () => {
  loadList.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "internal_error",
      status: 500,
      traceId: "trace-list",
    },
  });

  renderWithMessages(
    await WorkspacesPage({ searchParams: Promise.resolve({}) }),
  );

  expect(screen.getByRole("alert")).toHaveTextContent(
    "Workspaces are unavailable",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-list");
  expect(screen.queryByText("internal_error")).not.toBeInTheDocument();
});

it("provides local list/dashboard loading and safe retry boundaries", async () => {
  let view = render(await WorkspacesLoading());
  expect(screen.getByRole("status")).toHaveTextContent("Loading workspaces");
  view.unmount();

  view = render(await OrganizationDashboardLoading());
  expect(screen.getByRole("status")).toHaveTextContent(
    "Loading workspace dashboard",
  );
  view.unmount();

  for (const Boundary of [WorkspacesError, OrganizationDashboardError]) {
    const reset = jest.fn();
    const boundary = renderWithMessages(
      <Boundary error={new Error("private detail")} reset={reset} />,
    );
    expect(screen.queryByText("private detail")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(reset).toHaveBeenCalledTimes(1);
    boundary.unmount();
  }
});
