import { screen } from "@testing-library/react";

import { AccountHeaderNavigation } from "@/src/components/account/account-header-navigation";
import { SiteHeader } from "@/src/features/application/ui/site-header";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/features/application/ui/theme-switcher", () => ({
  ThemeSwitcher: () => (
    <button aria-label="Toggle theme" disabled type="button" />
  ),
}));

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
    expect(screen.getByRole("banner").firstElementChild).toHaveClass("min-w-0");
    expect(
      screen.queryByRole("link", { name: "Account settings" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/sign in/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/workspace/i)).not.toBeInTheDocument();
  });

  it("renders route-owned authenticated header content without duplicating account landmarks", () => {
    renderWithMessages(
      <SiteHeader
        accountNavigation={<AccountHeaderNavigation />}
        organizationSwitcher={<p>Server-owned workspace switcher</p>}
      />,
    );

    expect(screen.getByText("Server-owned workspace switcher")).toBeVisible();
    expect(
      screen.queryByRole("navigation", { name: "Account settings" }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Account settings" }),
    ).toHaveAttribute("href", "/user/profile");
  });
});
