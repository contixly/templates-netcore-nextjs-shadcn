import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";

import ProtectedLayout from "@/src/app/(protected)/layout";

const protectedApplicationShell = jest.fn(
  ({
    children,
    navigation,
  }: {
    children: ReactNode;
    navigation: ReactNode;
  }) => (
    <div data-testid="protected-shell">
      {navigation}
      <main id="main-content">{children}</main>
    </div>
  ),
);

jest.mock("next/headers", () => ({
  cookies: async () => ({
    toString: (): string => "template.sidebar=open",
  }),
}));
jest.mock("@/src/features/application/ui/protected-application-shell", () => ({
  ProtectedApplicationShell: (props: {
    children: ReactNode;
    defaultSidebarOpen: boolean;
    navigation: ReactNode;
  }) => protectedApplicationShell(props),
}));

it("renders one route-aware navigation slot and one main-content target", async () => {
  const applicationNavigation = (
    <nav data-slot="application-navigation">Navigation</nav>
  );

  render(
    await ProtectedLayout({
      children: <article>Protected page</article>,
      applicationNavigation,
    }),
  );

  expect(
    screen
      .getAllByRole("navigation")
      .filter((node) => node.dataset.slot === "application-navigation"),
  ).toHaveLength(1);
  expect(document.querySelectorAll("#main-content")).toHaveLength(1);
  expect(protectedApplicationShell).toHaveBeenCalledWith(
    expect.objectContaining({ defaultSidebarOpen: true }),
  );
  expect(document.body.innerHTML).not.toContain("HomePage");
});
