import { fireEvent, render, screen } from "@testing-library/react";

import { ApplicationSidebar } from "@/src/features/application/ui/application-sidebar";
import { SidebarProvider, useSidebar } from "@/src/components/ui/sidebar";
import { TooltipProvider } from "@/src/components/ui/tooltip";
import type { ApplicationShellData } from "@/src/features/application/application-shell-model";

let mockIsMobile = false;

const shellData = {
  account: {
    id: "account-id",
    displayName: "Ada Lovelace",
    primaryEmail: "ada@example.test",
    imageUrl: null,
    createdAt: "2026-08-03T10:00:00Z",
    verifiedEmails: [],
  },
  currentOrganization: {
    id: "acme-id",
    name: "Acme",
    slug: "acme",
    canonicalKey: "acme",
    createdAt: "2026-08-03T10:00:00Z",
    updatedAt: "2026-08-03T10:00:00Z",
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
  nextOrganizationCursor: null,
  organizations: [],
  session: {
    id: "session-id",
    createdAt: "2026-08-03T10:00:00Z",
    updatedAt: "2026-08-03T10:00:00Z",
    expiresAt: "2026-08-04T10:00:00Z",
    activeOrganizationId: "acme-id",
  },
  user: {
    id: "user-id",
    name: "Ada Lovelace",
    email: "ada@example.test",
    emailVerified: true,
    image: null,
  },
} satisfies ApplicationShellData;

jest.mock("next/navigation", () => ({
  usePathname: () => "/w/acme/dashboard",
  useRouter: () => ({
    push: jest.fn(),
    refresh: jest.fn(),
    replace: jest.fn(),
  }),
}));
jest.mock("next-intl", () => ({
  useTranslations: () => (key: string) =>
    ({
      account: "Account",
      brandHomeLabel: "Application Template home",
      close: "Close sidebar",
      dashboard: "Dashboard",
      documentation: "Documentation",
      mobileDescription: "Navigate the application safely.",
      mobileTitle: "Application navigation",
      workspace: "Workspace",
      workspaces: "Workspaces",
      open: "Open sidebar",
    })[key] ?? key,
}));
jest.mock("@/src/hooks/use-mobile", () => ({
  useIsMobile: () => mockIsMobile,
}));

jest.mock("@/src/components/organizations/organization-switcher", () => ({
  OrganizationSwitcher: ({
    activeOrganizationId,
    currentOrganization,
    onNavigate,
    organizations,
  }: {
    activeOrganizationId?: string | null;
    currentOrganization?: { id: string; name: string } | null;
    onNavigate?: () => void;
    organizations: readonly { id: string; name: string }[];
  }) => {
    const current =
      organizations.find(({ id }) => id === activeOrganizationId) ??
      currentOrganization;
    return current ? (
      <div>
        <button type="button">Current workspace: {current.name}</button>
        <button onClick={onNavigate} type="button">
          Complete workspace navigation
        </button>
      </div>
    ) : null;
  },
}));

jest.mock("@/src/features/authentication/ui/logout-button", () => ({
  LogoutButton: () => <button type="button">Log out</button>,
}));
jest.mock("@/src/components/organizations/organization-create-dialog", () => ({
  OrganizationCreateDialog: ({ onNavigate }: { onNavigate?: () => void }) => (
    <div>
      <button type="button">Create workspace</button>
      <button onClick={onNavigate} type="button">
        Complete workspace creation
      </button>
    </div>
  ),
}));

beforeEach(() => {
  mockIsMobile = false;
});

it("renders organization-aware primary, documentation, and account navigation", () => {
  render(<ApplicationSidebar data={shellData} pathname="/w/acme/dashboard" />);

  expect(screen.getByRole("link", { name: "Dashboard" })).toHaveAttribute(
    "href",
    "/w/acme/dashboard",
  );
  expect(screen.getByRole("link", { name: "Documentation" })).toHaveAttribute(
    "href",
    "/docs",
  );
  expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "Current workspace: Acme" }),
  ).toBeEnabled();
  expect(screen.getByRole("button", { name: "Log out" })).toBeEnabled();
});

it.each(["/workspaces", "/welcome", "/user/profile"])(
  "keeps the active workspace switcher in the sidebar on %s",
  (pathname) => {
    const globalShellData: ApplicationShellData = {
      ...shellData,
      currentOrganization: null,
      organizations: [shellData.currentOrganization],
    };

    render(<ApplicationSidebar data={globalShellData} pathname={pathname} />);

    expect(
      screen.getByRole("button", { name: "Current workspace: Acme" }),
    ).toBeVisible();
  },
);

it("labels the desktop rail from the current collapsed state", () => {
  render(
    <TooltipProvider>
      <SidebarProvider defaultOpen={false}>
        <ApplicationSidebar data={shellData} pathname="/w/acme/dashboard" />
      </SidebarProvider>
    </TooltipProvider>,
  );

  fireEvent.click(screen.getByRole("button", { name: "Open sidebar" }));
  expect(
    screen.getByRole("button", { name: "Close sidebar" }),
  ).toBeInTheDocument();
});

it("keeps the collapsed brand link accessible through a localized name", () => {
  render(
    <TooltipProvider>
      <SidebarProvider defaultOpen={false}>
        <ApplicationSidebar data={shellData} pathname="/w/acme/dashboard" />
      </SidebarProvider>
    </TooltipProvider>,
  );

  const brandLink = screen.getByRole("link", {
    name: "Application Template home",
  });
  expect(brandLink).toHaveAttribute("href", "/w/acme/dashboard");
  expect(brandLink).toHaveAccessibleName("Application Template home");
  expect(
    screen.queryByText("Application Template home"),
  ).not.toBeInTheDocument();
});

it("provides a focusable localized close action inside the mobile sheet", () => {
  mockIsMobile = true;

  function OpenMobileSidebar() {
    const { setOpenMobile } = useSidebar();
    return (
      <button onClick={() => setOpenMobile(true)} type="button">
        Open mobile navigation
      </button>
    );
  }

  render(
    <TooltipProvider>
      <SidebarProvider>
        <OpenMobileSidebar />
        <ApplicationSidebar data={shellData} pathname="/w/acme/dashboard" />
      </SidebarProvider>
    </TooltipProvider>,
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Open mobile navigation" }),
  );
  expect(screen.getByRole("dialog")).toHaveAccessibleName(
    "Application navigation",
  );
  expect(screen.getByRole("dialog")).toHaveAccessibleDescription(
    "Navigate the application safely.",
  );
  const close = screen.getByRole("button", { name: "Close sidebar" });
  expect(close).toHaveAccessibleName("Close sidebar");
  close.focus();
  expect(close).toHaveFocus();

  fireEvent.click(close);
  expect(
    screen.queryByRole("button", { name: "Close sidebar" }),
  ).not.toBeInTheDocument();
});

it.each([
  ["Template", "link"],
  ["Complete workspace navigation", "button"],
  ["Complete workspace creation", "button"],
] as const)(
  "closes mobile navigation after %s without changing desktop state",
  (name, role) => {
    mockIsMobile = true;

    function MobileState() {
      const { setOpenMobile, state } = useSidebar();
      return (
        <>
          <button onClick={() => setOpenMobile(true)} type="button">
            Open mobile navigation
          </button>
          <output aria-label="Desktop sidebar state">{state}</output>
        </>
      );
    }

    render(
      <TooltipProvider>
        <SidebarProvider defaultOpen={false}>
          <MobileState />
          <ApplicationSidebar data={shellData} pathname="/w/acme/dashboard" />
        </SidebarProvider>
      </TooltipProvider>,
    );

    fireEvent.click(
      screen.getByRole("button", { name: "Open mobile navigation" }),
    );
    fireEvent.click(screen.getByRole(role, { name }));

    expect(
      screen.queryByRole("button", { name: "Close sidebar" }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("status", { name: "Desktop sidebar state" }),
    ).toHaveTextContent("collapsed");
  },
);
