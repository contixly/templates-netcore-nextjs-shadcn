import { screen } from "@testing-library/react";

import { SiteHeader } from "@/src/components/application/site-header";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/components/application/theme-switcher", () => ({
  ThemeSwitcher: () => (
    <button aria-label="Toggle theme" disabled type="button" />
  ),
}));

describe("SiteHeader", () => {
  it("contains only brand, root navigation, and theme control", () => {
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
});
