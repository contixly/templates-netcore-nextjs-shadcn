import { screen } from "@testing-library/react";

import { SiteHeader } from "@/src/components/application/site-header";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/components/application/theme-switcher", () => ({
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
    expect(screen.queryByText(/sign in/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/workspace/i)).not.toBeInTheDocument();
  });

  it("renders the route-owned organization slot inside the header", () => {
    renderWithMessages(
      <SiteHeader
        organizationSwitcher={<p>Server-owned workspace switcher</p>}
      />,
    );

    expect(screen.getByText("Server-owned workspace switcher")).toBeVisible();
  });
});
