import { isValidElement } from "react";
import { render, screen } from "@testing-library/react";

import OrganizationDashboardLoading from "@/src/app/(protected)/w/[organizationKey]/dashboard/loading";
import OrganizationDashboardPage from "@/src/app/(protected)/w/[organizationKey]/dashboard/page";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";
import { mockDashboardGeometry } from "@/test/support/dashboard-geometry";

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
}));
jest.mock("next-intl/server", () => ({
  getLocale: async () => "en",
  getTranslations: async (namespace: string) => {
    const values: Record<string, string> = {
      "organizations.pages.dashboard.loading": "Loading workspace dashboard",
    };
    return (key: string) => values[`${namespace}.${key}`] ?? key;
  },
}));
jest.mock("@/src/features/authentication/load-protected-session", () => ({
  loadProtectedSession: jest.fn(),
}));
jest.mock("@/src/lib/api/organizations/server/load-organization", () => ({
  loadOrganization: jest.fn(),
}));
jest.mock("@/src/lib/api/organizations/server/load-organizations", () => ({
  loadOrganizations: jest.fn(),
}));

const loadSession = jest.mocked(loadProtectedSession);
const loadDetail = jest.mocked(loadOrganization);
const loadList = jest.mocked(loadOrganizations);
let restoreGeometry: () => void;

beforeEach(() => {
  restoreGeometry = mockDashboardGeometry();
  jest.clearAllMocks();
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
        createdAt: "2026-08-01T10:00:00Z",
        updatedAt: "2026-08-01T10:00:00Z",
        expiresAt: "2026-08-04T10:00:00Z",
        activeOrganizationId: "organization-id",
      },
    },
  });
  loadDetail.mockResolvedValue({
    ok: true,
    data: {
      id: "organization-id",
      name: "Acme",
      slug: "acme",
      canonicalKey: "acme",
      createdAt: "2026-08-01T10:00:00Z",
      updatedAt: "2026-08-01T10:00:00Z",
      accessPrincipal: "user",
      currentRole: "owner",
      allowedEmailDomains: [],
      capabilities: {
        canUpdateOrganization: true,
        canDeleteOrganization: true,
        canAddMembers: true,
        canUpdateMemberRoles: true,
        canManageTeams: true,
        canManageInvitations: true,
        canManageApiKeys: true,
      },
    },
  });
  loadList.mockResolvedValue({
    ok: true,
    data: { items: [], nextCursor: null },
  });
});

afterEach(() => restoreGeometry());

it("passes only RSC-serializable dashboard props into the client boundary", async () => {
  const page = await OrganizationDashboardPage({
    params: Promise.resolve({ organizationKey: "acme" }),
  });

  expect(isValidElement(page)).toBe(true);
  const copy = (
    page as React.ReactElement<{
      copy: {
        cards: { revenue: { detail: string; trend?: string } };
        table: { add?: string };
      };
    }>
  ).props.copy;
  expect(JSON.parse(JSON.stringify(copy))).toEqual(copy);
  expect(copy.cards.revenue).toEqual({
    detail: "Visitors for the last 6 months",
    label: "Total revenue",
    trend: "Trending up this month",
  });
  expect(copy.table.add).toBe("Add section");
});

it("replaces only canonical organization dashboard presentation", async () => {
  render(
    await OrganizationDashboardPage({
      params: Promise.resolve({ organizationKey: "acme" }),
    }),
  );

  expect(screen.getByText("$1,250.00")).toBeVisible();
  expect(
    screen.getByRole("region", { name: "Dashboard metrics" }).parentElement,
  ).toHaveClass("@container/main");
  expect(screen.getByRole("img", { name: "Total visitors" })).toBeVisible();
  expect(screen.getByRole("table", { name: "Sections" })).toBeVisible();
  expect(loadSession).toHaveBeenCalledTimes(1);
  expect(loadDetail).toHaveBeenCalledTimes(1);
  expect(loadList).toHaveBeenCalledTimes(1);
});

it("mirrors the dashboard regions in its accessible loading skeleton", async () => {
  render(await OrganizationDashboardLoading());

  const status = screen.getByRole("status");
  expect(status).toHaveAttribute("aria-busy", "true");
  expect(status).toHaveClass("@container/main");
  expect(
    status.querySelectorAll('[data-testid="dashboard-card-skeleton"]'),
  ).toHaveLength(4);
  expect(
    status.querySelector('[data-testid="dashboard-chart-skeleton"]'),
  ).not.toBeNull();
  expect(
    status.querySelector('[data-slot="dashboard-table-skeleton"]'),
  ).not.toBeNull();
});
