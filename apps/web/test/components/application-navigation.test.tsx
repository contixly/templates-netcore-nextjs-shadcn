import { fireEvent, render, screen } from "@testing-library/react";
import type { ReactNode } from "react";

import { AccountNavigation } from "@/src/components/application/account-navigation";
import { PrimaryNavigation } from "@/src/components/application/primary-navigation";
import { useMobileSidebarClose } from "@/src/hooks/use-mobile-sidebar-close";

const setOpenMobile = jest.fn();
const toggleSidebar = jest.fn();

jest.mock("@/src/components/ui/sidebar", () => ({
  SidebarMenu: ({ children }: { children: ReactNode }) => <ul>{children}</ul>,
  SidebarMenuButton: ({
    children,
    isActive,
  }: {
    children: ReactNode;
    isActive?: boolean;
  }) => <div data-active={isActive ? "true" : "false"}>{children}</div>,
  SidebarMenuItem: ({ children }: { children: ReactNode }) => (
    <li>{children}</li>
  ),
  useSidebar: () => ({
    isMobile: true,
    setOpenMobile,
    toggleSidebar,
  }),
}));
jest.mock("next-intl", () => ({
  useTranslations: () => (key: string) =>
    ({
      dashboard: "Dashboard",
      workspaces: "Workspaces",
      documentation: "Documentation",
    })[key] ?? key,
}));
jest.mock("@/src/components/organizations/organization-create-dialog", () => ({
  OrganizationCreateDialog: () => (
    <button type="button">Create workspace</button>
  ),
}));
jest.mock("@/src/components/authentication/logout-button", () => ({
  LogoutButton: () => <button type="button">Log out</button>,
}));

beforeEach(() => {
  setOpenMobile.mockReset();
  toggleSidebar.mockReset();
});

it("closes mobile navigation explicitly without toggling desktop state", () => {
  function CloseButton() {
    const closeMobileSidebar = useMobileSidebarClose();
    return (
      <button onClick={closeMobileSidebar} type="button">
        Navigate
      </button>
    );
  }

  render(<CloseButton />);
  fireEvent.click(screen.getByRole("button", { name: "Navigate" }));

  expect(setOpenMobile).toHaveBeenCalledWith(false);
  expect(toggleSidebar).not.toHaveBeenCalled();
});

it("marks the current primary route and closes after navigation", () => {
  render(
    <PrimaryNavigation
      dashboardHref="/w/acme/dashboard"
      pathname="/w/acme/dashboard"
    />,
  );

  const dashboard = screen.getByRole("link", { name: "Dashboard" });
  expect(dashboard).toHaveAttribute("aria-current", "page");
  fireEvent.click(screen.getByRole("link", { name: "Workspaces" }));
  expect(setOpenMobile).toHaveBeenCalledWith(false);
  expect(toggleSidebar).not.toHaveBeenCalled();
});

it("highlights broad sections without claiming a non-matching URL is current", () => {
  const { rerender } = render(
    <PrimaryNavigation dashboardHref="/dashboard" pathname="/welcome" />,
  );

  const workspaces = screen.getByRole("link", { name: "Workspaces" });
  expect(workspaces.parentElement).toHaveAttribute("data-active", "true");
  expect(workspaces).not.toHaveAttribute("aria-current");

  rerender(
    <AccountNavigation
      account={{
        id: "account-id",
        displayName: "Ada Lovelace",
        primaryEmail: "ada@example.test",
        imageUrl: null,
        createdAt: "2026-08-03T10:00:00Z",
        verifiedEmails: [],
      }}
      pathname="/user/security"
    />,
  );

  const account = screen.getByRole("link", { name: /Ada Lovelace/ });
  expect(account.parentElement).toHaveAttribute("data-active", "true");
  expect(account).not.toHaveAttribute("aria-current");
});
