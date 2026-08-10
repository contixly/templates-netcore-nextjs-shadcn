import { screen } from "@testing-library/react";

import { DocumentsShell } from "@/src/features/documents/ui/documents-shell";
import { renderWithMessages } from "@/test/support/render";

jest.mock("next/navigation", () => ({
  usePathname: () => "/docs/general/quick-start",
  useRouter: () => ({ push: jest.fn() }),
}));

test("documentation shell uses the reference sidebar width and scroll container", () => {
  renderWithMessages(
    <DocumentsShell navigation={[]} pageNavigationByHref={{}}>
      <article>Article</article>
    </DocumentsShell>,
  );

  expect(document.querySelector("[data-slot='sidebar-wrapper']")).toHaveStyle({
    "--sidebar-width": "24rem",
  });
  expect(screen.getByRole("main")).toHaveAttribute(
    "data-documents-scroll-container",
  );
});
