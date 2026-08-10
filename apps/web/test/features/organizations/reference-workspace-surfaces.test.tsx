import { fireEvent, screen } from "@testing-library/react";

import { OrganizationSettingsNav } from "@/src/features/organizations/ui/organization-settings-nav";
import { renderWithMessages } from "@/test/support/render";

beforeEach(() => {
  Object.defineProperty(window, "matchMedia", {
    configurable: true,
    value: jest.fn().mockImplementation((query: string) => ({
      addEventListener: jest.fn(),
      addListener: jest.fn(),
      dispatchEvent: jest.fn(),
      matches: false,
      media: query,
      onchange: null,
      removeEventListener: jest.fn(),
      removeListener: jest.fn(),
    })),
  });
});

test("workspace settings navigation uses the reference 16-rem desktop sidebar rail", () => {
  renderWithMessages(
    <OrganizationSettingsNav
      canManageApiKeys
      canManageInvitations
      organizationKey="acme"
      pathname="/w/acme/settings/workspace"
    />,
  );

  const navigation = screen.getByRole("navigation", {
    name: "Workspace settings",
  });
  expect(navigation).toHaveClass("md:w-64");
  expect(
    screen.getByRole("heading", { level: 2, name: "Workspace settings" }),
  ).toBeVisible();
  expect(
    navigation.closest('[data-slot="organization-settings-sidebar"]'),
  ).toHaveClass("h-full");
  expect(screen.getByRole("link", { name: "Workspace" })).toHaveAttribute(
    "aria-current",
    "page",
  );

  const mobileAction = screen.getByRole("button", {
    name: "Open workspace settings",
  });
  expect(mobileAction.parentElement).toHaveClass("justify-end");
  fireEvent.click(mobileAction);
  expect(screen.getByText("Choose a workspace settings page.")).toHaveClass(
    "sr-only",
  );
  expect(
    screen.queryByText("Loading workspace settings"),
  ).not.toBeInTheDocument();
});
