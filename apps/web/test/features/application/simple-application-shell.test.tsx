import { screen } from "@testing-library/react";

import { SimpleApplicationShell } from "@/src/features/application/ui/simple-application-shell";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/features/application/ui/theme-switcher", () => ({
  ThemeSwitcher: () => <button type="button">Toggle theme</button>,
}));

test("renders the feature-owned simple header around route content", () => {
  renderWithMessages(
    <SimpleApplicationShell>
      <main>Authentication content</main>
    </SimpleApplicationShell>,
  );

  const header = screen.getByRole("banner");
  expect(header).toHaveClass("md:sticky", "md:top-0");
  expect(header).not.toHaveClass("sticky", "top-0");
  expect(screen.getByRole("link", { name: "Return home" })).toHaveAttribute(
    "href",
    "/",
  );
  expect(screen.getByRole("link", { name: "Documentation" })).toHaveAttribute(
    "href",
    "/docs",
  );
  expect(screen.getByRole("button", { name: "Toggle theme" })).toBeEnabled();
  expect(screen.getByRole("main")).toHaveTextContent("Authentication content");
});
