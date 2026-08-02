import { fireEvent, screen, within } from "@testing-library/react";

import { DocumentsPage } from "@/src/components/documents/documents-page";
import {
  findActiveDocumentHeading,
  readDocumentHash,
} from "@/src/components/documents/documents-scroll-spy";
import type { DocumentInfo } from "@/src/features/documents/documents-types";
import { renderWithMessages } from "@/test/support/render";

const document = {
  sourcePath: "general/sample.en.mdx",
  canonicalSourcePath: "general/sample.mdx",
  canonicalUrl: "general/sample",
  requestedLocale: "en",
  contentLocale: "ru",
  isLocaleFallback: true,
  hasExplicitLocale: true,
  availableLocales: ["ru"],
  slug: ["general", "sample"],
  href: "/docs/general/sample",
  headings: [
    { level: 2, title: "First section", id: "first-section" },
    { level: 3, title: "Nested section", id: "nested-section" },
    { level: 2, title: "Second section", id: "second-section" },
  ],
  meta: {
    title: "Documentation sample",
    description: "A complete article.",
    group: "Documentation",
    parentItem: "Authoring",
    order: 30,
    status: "review",
    hide: true,
    toc: true,
    purpose: "Content authors",
    author: "Template Maintainers",
    version: "1.2.0",
    editedAt: "2026-07-23",
    reading: "8 min",
  },
} satisfies DocumentInfo;

function Content() {
  return <p>Imported MDX body</p>;
}

it("renders article metadata, fallback/status markers, body, and generated h2 TOC", () => {
  renderWithMessages(<DocumentsPage Content={Content} document={document} />);

  const article = screen.getByRole("article", { name: "Documentation sample" });
  expect(article).toHaveTextContent("A complete article.");
  expect(article).toHaveTextContent("Imported MDX body");
  expect(article).toHaveTextContent("Documentation");
  expect(article).toHaveTextContent("Authoring");
  expect(article).toHaveTextContent("Content authors");
  expect(article).toHaveTextContent("Template Maintainers");
  expect(article).toHaveTextContent("1.2.0");
  expect(article).toHaveTextContent("8 min");
  expect(screen.getByText("2026-07-23")).toHaveAttribute(
    "datetime",
    "2026-07-23",
  );
  expect(article).toHaveTextContent("Review");
  expect(article).toHaveTextContent("Hidden");
  expect(article).toHaveTextContent("Russian");
  expect(article).toHaveTextContent("Showing Russian fallback");

  const toc = screen.getByRole("navigation", { name: "On this page" });
  expect(
    within(toc).getByRole("link", { name: "First section" }),
  ).toHaveAttribute("href", "#first-section");
  expect(within(toc).queryByText("Nested section")).not.toBeInTheDocument();
});

it("does not render the TOC when frontmatter disables it", () => {
  renderWithMessages(
    <DocumentsPage
      Content={Content}
      document={{ ...document, meta: { ...document.meta, toc: false } }}
    />,
  );

  expect(
    screen.queryByRole("navigation", { name: "On this page" }),
  ).not.toBeInTheDocument();
});

it("reads malformed hashes safely and determines activity inside the docs container", () => {
  expect(readDocumentHash("#second-section")).toBe("second-section");
  expect(readDocumentHash("#%E0%A4%A")).toBeUndefined();

  const container = documentNode("div", { top: 100 });
  const first = documentNode("h2", { top: 40 }, "first-section");
  const second = documentNode("h2", { top: 280 }, "second-section");
  container.append(first, second);
  globalThis.document.body.append(container);

  expect(
    findActiveDocumentHeading(container, ["first-section", "second-section"]),
  ).toBe("first-section");

  fireEvent.scroll(container);
  container.remove();
});

function documentNode(
  tag: string,
  rect: { top: number },
  id?: string,
): HTMLElement {
  const element = globalThis.document.createElement(tag);
  if (id) element.id = id;
  element.getBoundingClientRect = () =>
    ({
      ...rect,
      bottom: rect.top,
      left: 0,
      right: 0,
      width: 0,
      height: 0,
      x: 0,
      y: rect.top,
      toJSON() {},
    }) as DOMRect;
  return element;
}
