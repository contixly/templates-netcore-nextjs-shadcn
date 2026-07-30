import { render, screen } from "@testing-library/react";

import {
  OrganizationSwitcherRuntime,
  SiteHeader,
} from "@/src/components/application/site-header";
import {
  OrganizationSwitcherProvider,
  OrganizationSwitcherSlot,
} from "@/src/components/organizations/organization-switcher-context";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
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
  OrganizationSwitcher: (props: {
    organizations: Array<Record<string, unknown>>;
  }) => (
    <>
      <p>workspace switcher {String(props.organizations[0]?.name)}</p>
      <pre data-testid="switcher-props">{JSON.stringify(props)}</pre>
    </>
  ),
}));
jest.mock("@/src/lib/api/auth/server/load-server-auth-session", () => ({
  loadServerAuthSession: jest.fn(),
}));
jest.mock("@/src/lib/api/organizations/server/load-organizations", () => ({
  loadOrganizations: jest.fn(),
}));
jest.mock("@/src/lib/api/organizations/server/load-organization", () => ({
  loadOrganization: jest.fn(),
}));

const loadSession = jest.mocked(loadServerAuthSession);
const loadDetail = jest.mocked(loadOrganization);
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
  it("keeps the base header free of organization reads until a workspace registers context", async () => {
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
    expect(screen.getByRole("banner").firstElementChild).toHaveClass("min-w-0");
    expect(screen.queryByText(/sign in/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/workspace/i)).not.toBeInTheDocument();
    await Promise.resolve();
    expect(loadSession).not.toHaveBeenCalled();
    expect(loadList).not.toHaveBeenCalled();
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

    const registration = await OrganizationSwitcherRuntime();
    render(
      <OrganizationSwitcherProvider>
        <OrganizationSwitcherSlot />
        {registration}
      </OrganizationSwitcherProvider>,
    );

    expect(await screen.findByText("workspace switcher Acme")).toBeVisible();
    expect(
      JSON.parse(screen.getByTestId("switcher-props").textContent ?? ""),
    ).toEqual({
      activeOrganizationId: "acme-id",
      nextCursor: "opaque",
      organizations: [{ id: "acme-id", name: "Acme", canonicalKey: "acme" }],
    });
    expect(loadSession).toHaveBeenCalledTimes(1);
    expect(loadList).toHaveBeenCalledTimes(1);
  });

  it("adds active context when it is beyond the first list page", async () => {
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
          activeOrganizationId: "off-page-id",
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
    loadDetail.mockResolvedValue({
      ok: true,
      data: {
        id: "off-page-id",
        name: "Workspace Fifty One",
        slug: "workspace-fifty-one",
        canonicalKey: "workspace-fifty-one",
        createdAt: "2026-07-30T10:00:00Z",
        updatedAt: "2026-07-30T10:00:00Z",
        currentRole: "member",
        capabilities: {
          canUpdateOrganization: false,
          canDeleteOrganization: false,
          canAddMembers: false,
          canUpdateMemberRoles: false,
        },
        allowedEmailDomains: [],
      },
    });

    const registration = await OrganizationSwitcherRuntime();
    render(
      <OrganizationSwitcherProvider>
        <OrganizationSwitcherSlot />
        {registration}
      </OrganizationSwitcherProvider>,
    );

    expect(await screen.findByText("workspace switcher Acme")).toBeVisible();
    expect(
      JSON.parse(screen.getByTestId("switcher-props").textContent ?? ""),
    ).toMatchObject({
      currentOrganization: {
        id: "off-page-id",
        name: "Workspace Fifty One",
        canonicalKey: "workspace-fifty-one",
      },
    });
    expect(loadDetail).toHaveBeenCalledWith("off-page-id");
  });
});
