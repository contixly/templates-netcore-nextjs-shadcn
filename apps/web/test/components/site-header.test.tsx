import { render, screen } from "@testing-library/react";

import {
  OrganizationSwitcherRuntime,
  SiteHeader,
} from "@/src/components/application/site-header";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";
import { renderWithMessages } from "@/test/support/render";

jest.mock("next/server", () => ({
  connection: jest.fn().mockResolvedValue(undefined),
}));
jest.mock("@/src/components/application/theme-switcher", () => ({
  ThemeSwitcher: () => (
    <button aria-label="Toggle theme" disabled type="button" />
  ),
}));
jest.mock("@/src/components/organizations/organization-switcher", () => ({
  OrganizationSwitcher: ({
    organizations,
  }: {
    organizations: Array<{ name: string }>;
  }) => <p>workspace switcher {organizations[0]?.name}</p>,
}));
jest.mock("@/src/lib/api/auth/server/load-server-auth-session", () => ({
  loadServerAuthSession: jest.fn(),
}));
jest.mock("@/src/lib/api/organizations/server/load-organizations", () => ({
  loadOrganizations: jest.fn(),
}));

const loadSession = jest.mocked(loadServerAuthSession);
const loadList = jest.mocked(loadOrganizations);

beforeEach(() => {
  jest.clearAllMocks();
  loadSession.mockResolvedValue({
    ok: true,
    data: { authenticated: false, user: null, session: null },
  });
  loadList.mockResolvedValue({
    ok: true,
    data: { items: [], nextCursor: null },
  });
});

describe("SiteHeader", () => {
  it("keeps the base brand, root navigation, and theme control", () => {
    renderWithMessages(<SiteHeader />);

    expect(screen.getByRole("link", { name: "Template" })).toHaveAttribute(
      "href",
      "/",
    );
    expect(screen.getByRole("link", { name: "Home" })).toHaveAttribute(
      "href",
      "/",
    );
    expect(screen.getByRole("button", { name: "Toggle theme" })).toBeDisabled();
    expect(screen.queryByText(/sign in/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/workspace/i)).not.toBeInTheDocument();
  });

  it("loads the minimal switcher projection only for an authenticated session", async () => {
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
          activeOrganizationId: "acme-id",
        },
      },
    });
    loadList.mockResolvedValue({
      ok: true,
      data: {
        items: [
          {
            id: "acme-id",
            name: "Acme",
            slug: "acme",
            canonicalKey: "acme",
            createdAt: "2026-07-30T10:00:00Z",
            updatedAt: "2026-07-30T10:00:00Z",
            currentRole: "owner",
            capabilities: {
              canUpdateOrganization: true,
              canDeleteOrganization: true,
              canAddMembers: true,
              canUpdateMemberRoles: true,
            },
          },
        ],
        nextCursor: "opaque",
      },
    });

    render(await OrganizationSwitcherRuntime());

    expect(screen.getByText("workspace switcher Acme")).toBeVisible();
    expect(loadSession).toHaveBeenCalledTimes(1);
    expect(loadList).toHaveBeenCalledTimes(1);
  });
});
