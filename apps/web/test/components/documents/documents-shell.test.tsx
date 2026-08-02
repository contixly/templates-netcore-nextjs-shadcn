import { fireEvent, screen, waitFor, within } from "@testing-library/react";

import { DocumentsShell } from "@/src/components/documents/documents-shell";
import type {
  DocumentPageNavigation,
  DocumentsSidebarGroup,
} from "@/src/features/documents/documents-types";
import { renderWithMessages, withMessages } from "@/test/support/render";

let mockPathname = "/docs/api/api-v1";

jest.mock("next/navigation", () => ({
  usePathname: () => mockPathname,
}));

const navigation = [
  {
    label: "General",
    parents: [
      {
        label: "Getting started",
        items: [
          {
            canonicalUrl: "general/quick-start",
            href: "/docs/general/quick-start",
            label: "Quick start",
            status: "published",
          },
        ],
      },
    ],
  },
  {
    label: "API and integrations",
    parents: [
      {
        label: "API reference",
        items: [
          {
            canonicalUrl: "api/api-keys",
            href: "/docs/api/api-keys",
            label: "API keys",
            status: "published",
          },
          {
            canonicalUrl: "api/api-v1",
            href: "/docs/api/api-v1",
            label: "API v1 reference",
            status: "published",
          },
        ],
      },
    ],
  },
] satisfies DocumentsSidebarGroup[];

const pageNavigationByHref = {
  "/docs/api/api-v1": {
    previous: {
      canonicalUrl: "api/api-keys",
      href: "/docs/api/api-keys",
      title: "API keys",
      description: "Create and manage API keys.",
    },
  },
} satisfies Record<string, DocumentPageNavigation>;

function renderShell() {
  return renderWithMessages(
    <DocumentsShell
      navigation={navigation}
      pageNavigationByHref={pageNavigationByHref}
    >
      <article>Document body</article>
    </DocumentsShell>,
  );
}

beforeEach(() => {
  mockPathname = "/docs/api/api-v1";
});

it("provides the main-content target and marks the current document", () => {
  renderShell();

  expect(screen.getByRole("main")).toHaveAttribute("id", "main-content");
  expect(screen.getByRole("main")).toHaveAttribute(
    "data-documents-scroll-container",
  );
  expect(
    screen.getByRole("link", { name: "API v1 reference" }),
  ).toHaveAttribute("aria-current", "page");
  expect(
    screen.getByRole("navigation", { name: "Documentation" }),
  ).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Home" })).toHaveAttribute(
    "href",
    "/",
  );
  expect(screen.getByRole("button", { name: "Search docs" })).toBeDisabled();
});

it("renders breadcrumb context and previous-document navigation", () => {
  renderShell();

  const breadcrumb = screen.getByRole("navigation", {
    name: "Documentation: API v1 reference",
  });
  expect(breadcrumb).toHaveTextContent("API and integrations");
  expect(breadcrumb).toHaveTextContent("API reference");
  expect(within(breadcrumb).getByText("API v1 reference")).toHaveAttribute(
    "aria-current",
    "page",
  );
  const previousLinks = screen.getAllByRole("link", {
    name: /Previous document.*API keys/i,
  });
  expect(previousLinks).toHaveLength(2);
  expect(previousLinks[0]).toHaveAttribute("href", "/docs/api/api-keys");
  expect(previousLinks[1]).toHaveAttribute("href", "/docs/api/api-keys");
});

it("automatically opens the active parent after the pathname changes", () => {
  const view = renderShell();
  const apiLink = screen.getByRole("link", { name: "API v1 reference" });

  expect(apiLink.closest("details")).toHaveAttribute("open");

  mockPathname = "/docs/general/quick-start";
  view.rerender(
    withMessages(
      <DocumentsShell
        navigation={navigation}
        pageNavigationByHref={pageNavigationByHref}
      >
        <article>Quick start body</article>
      </DocumentsShell>,
    ),
  );

  expect(
    screen.getByRole("link", { name: "Quick start" }).closest("details"),
  ).toHaveAttribute("open");
});

it("closes mobile navigation after a document is selected", async () => {
  renderShell();

  fireEvent.click(screen.getByRole("button", { name: "Open navigation" }));
  const dialog = screen.getByRole("dialog", { name: "Documentation" });
  const link = within(dialog).getByRole("link", { name: "Quick start" });
  link.addEventListener("click", (event) => event.preventDefault());
  fireEvent.click(link);

  await waitFor(() => {
    expect(
      screen.queryByRole("dialog", { name: "Documentation" }),
    ).not.toBeInTheDocument();
  });
});
