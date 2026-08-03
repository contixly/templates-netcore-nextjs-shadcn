import { Children, type ReactElement, type ReactNode } from "react";

import ProtectedLayout from "@/src/app/(protected)/layout";

function asElement<Props extends object>(node: ReactNode): ReactElement<Props> {
  if (typeof node !== "object" || node === null || !("type" in node)) {
    throw new Error("Expected a React element");
  }

  return node as ReactElement<Props>;
}

it("renders one route-aware navigation slot and one main-content target", () => {
  const applicationNavigation = (
    <nav data-slot="application-navigation">Navigation</nav>
  );
  const layout = asElement<{ children: ReactNode }>(
    ProtectedLayout({
      children: <article>Protected page</article>,
      applicationNavigation,
    }),
  );
  const nodes = Children.toArray(layout.props.children).map((node) =>
    asElement<Record<string, unknown>>(node),
  );

  expect(
    nodes.filter(
      ({ props }) => props["data-slot"] === "application-navigation",
    ),
  ).toHaveLength(1);
  expect(nodes.filter(({ props }) => props.id === "main-content")).toHaveLength(
    1,
  );
  expect(JSON.stringify(layout)).not.toContain("HomePage");
});
