import { fireEvent, screen } from "@testing-library/react";
import { renderToStaticMarkup } from "react-dom/server";

import { ThemeSwitcher } from "@/src/features/application/ui/theme-switcher";
import { renderWithMessages, withMessages } from "@/test/support/render";

const mockSetTheme = jest.fn();

jest.mock("next-themes", () => ({
  useTheme: () => ({
    resolvedTheme: "light",
    setTheme: mockSetTheme,
  }),
}));

beforeEach(() => {
  mockSetTheme.mockClear();
});

describe("ThemeSwitcher", () => {
  it("renders stable disabled markup before hydration", () => {
    const markup = renderToStaticMarkup(withMessages(<ThemeSwitcher />));

    expect(markup).toContain("disabled");
    expect(markup).toContain('data-interaction-ready="false"');
    expect(markup).toContain('data-slot="button"');
    expect(markup).toContain("Toggle theme");
  });

  it("switches from resolved light to dark after hydration", async () => {
    renderWithMessages(<ThemeSwitcher />);

    const button = screen.getByRole("button", {
      name: "Switch to dark theme",
    });
    expect(button).toBeEnabled();
    expect(button).toHaveAttribute("data-interaction-ready", "true");
    expect(button).toHaveAttribute("data-slot", "button");

    fireEvent.click(button);

    expect(mockSetTheme).toHaveBeenCalledWith("dark");
  });
});
