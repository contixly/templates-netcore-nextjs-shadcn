import { renderToString } from "react-dom/server";
import { screen } from "@testing-library/react";

import NonWorkspaceSwitcherSlotPage from "@/src/app/(site)/@organizationSwitcher/workspaces/page";
import WorkspaceSwitcherSlotPage from "@/src/app/(site)/@organizationSwitcher/w/[organizationKey]/dashboard/page";
import { SiteHeader } from "@/src/components/application/site-header";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import type {
  OrganizationDetailResponse,
  OrganizationSummaryResponse,
} from "@/src/lib/api/generated/types.gen";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";
import { renderWithMessages, withMessages } from "@/test/support/render";

const pathname = jest.fn(() => "/w/acme/dashboard");

jest.mock("next/navigation", () => ({
  usePathname: () => pathname(),
  useRouter: () => ({ push: jest.fn(), refresh: jest.fn() }),
}));
jest.mock("next/server", () => ({
  connection: jest.fn().mockResolvedValue(undefined),
}));
jest.mock("@/src/components/application/theme-switcher", () => ({
  ThemeSwitcher: () => <button aria-label="Toggle theme" type="button" />,
}));
jest.mock("@/src/lib/api/auth/server/load-server-auth-session", () => ({
  loadServerAuthSession: jest.fn(),
}));
jest.mock("@/src/lib/api/organizations/server/load-organization", () => ({
  loadOrganization: jest.fn(),
}));
jest.mock("@/src/lib/api/organizations/server/load-organizations", () => ({
  loadOrganizations: jest.fn(),
}));

const loadSession = jest.mocked(loadServerAuthSession);
const loadDetail = jest.mocked(loadOrganization);
const loadList = jest.mocked(loadOrganizations);
const capabilities = {
  canUpdateOrganization: true,
  canDeleteOrganization: true,
  canAddMembers: true,
  canUpdateMemberRoles: true,
  canManageTeams: true,
  canManageInvitations: true,
  canManageApiKeys: true,
};

function summary(
  id: string,
  name: string,
  canonicalKey: string,
  canManageInvitations = false,
): Extract<OrganizationSummaryResponse, { accessPrincipal: "user" }> {
  return {
    id,
    name,
    slug: canonicalKey,
    canonicalKey,
    createdAt: "2026-07-30T10:00:00Z",
    updatedAt: "2026-07-30T10:00:00Z",
    accessPrincipal: "user" as const,
    currentRole: "owner",
    capabilities: { ...capabilities, canManageInvitations },
  };
}

function detail(
  id: string,
  name: string,
  canonicalKey: string,
): OrganizationDetailResponse {
  return {
    ...summary(id, name, canonicalKey, true),
    allowedEmailDomains: [],
  };
}

beforeEach(() => {
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
        createdAt: "2026-07-30T10:00:00Z",
        updatedAt: "2026-07-30T10:00:00Z",
        expiresAt: "2026-08-01T10:00:00Z",
        activeOrganizationId: null,
      },
    },
  });
  loadList.mockResolvedValue({
    ok: true,
    data: {
      items: [summary("first-id", "First Page", "first-page")],
      nextCursor: "page-two",
    },
  });
});

async function workspaceSlot(organizationKey: string, name: string) {
  loadDetail.mockResolvedValueOnce({
    ok: true,
    data: detail(`${organizationKey}-id`, name, organizationKey),
  });
  return WorkspaceSwitcherSlotPage({
    params: Promise.resolve({
      organizationKey,
      path: ["dashboard"],
    }),
  });
}

it("includes the resolved off-first-page current workspace in initial server HTML", async () => {
  pathname.mockReturnValue("/w/acme/dashboard");
  const organizationSwitcher = await workspaceSlot("acme", "Acme Current");

  const html = renderToString(
    withMessages(<SiteHeader organizationSwitcher={organizationSwitcher} />),
  );

  expect(html).toContain("Current workspace: Acme Current");
  expect(html).not.toContain("Current workspace: First Page");
});

it("serializes only compact first-page and current switcher projections", async () => {
  const organizationSwitcher = await workspaceSlot("acme", "Acme Current");

  expect(organizationSwitcher).toMatchObject({
    props: {
      currentOrganization: {
        canonicalKey: "acme",
        id: "acme-id",
        name: "Acme Current",
        canManageInvitations: true,
      },
      organizations: [
        {
          canonicalKey: "first-page",
          id: "first-id",
          name: "First Page",
          canManageInvitations: false,
        },
      ],
    },
  });
  expect(
    Object.keys(
      (organizationSwitcher as { props: { currentOrganization: object } }).props
        .currentOrganization,
    ).sort(),
  ).toEqual(["canManageInvitations", "canonicalKey", "id", "name"]);
  expect(
    Object.keys(
      (organizationSwitcher as { props: { organizations: object[] } }).props
        .organizations[0] ?? {},
    ).sort(),
  ).toEqual(["canManageInvitations", "canonicalKey", "id", "name"]);
});

it("replaces A with B atomically during a workspace soft-route transition", async () => {
  pathname.mockReturnValue("/w/a/dashboard");
  const slotA = await workspaceSlot("a", "Workspace A");
  const view = renderWithMessages(<SiteHeader organizationSwitcher={slotA} />);
  expect(
    screen.getByRole("button", { name: "Current workspace: Workspace A" }),
  ).toBeVisible();

  pathname.mockReturnValue("/w/b/dashboard");
  const slotB = await workspaceSlot("b", "Workspace B");
  view.rerender(withMessages(<SiteHeader organizationSwitcher={slotB} />));

  expect(
    screen.getByRole("button", { name: "Current workspace: Workspace B" }),
  ).toBeVisible();
  expect(screen.queryByText(/Workspace A/)).not.toBeInTheDocument();
});

it("clears the server-owned slot when navigating away from workspace routes", async () => {
  pathname.mockReturnValue("/w/acme/dashboard");
  const workspace = await workspaceSlot("acme", "Acme Current");
  const view = renderWithMessages(
    <SiteHeader organizationSwitcher={workspace} />,
  );
  expect(
    screen.getByRole("button", { name: "Current workspace: Acme Current" }),
  ).toBeVisible();

  pathname.mockReturnValue("/workspaces");
  const cleared = await NonWorkspaceSwitcherSlotPage();
  view.rerender(withMessages(<SiteHeader organizationSwitcher={cleared} />));

  expect(
    screen.queryByRole("button", { name: /current workspace/i }),
  ).not.toBeInTheDocument();
});
