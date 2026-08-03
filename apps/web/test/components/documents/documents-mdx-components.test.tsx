import { act, fireEvent, render, screen, within } from "@testing-library/react";
import type { ComponentType, ReactNode } from "react";

import { createDocumentMdxComponents } from "@/src/components/documents/mdx/documents-mdx-components";
import type { DocumentInfo } from "@/src/features/documents/documents-types";

const document = {
  sourcePath: "index.en.mdx",
  canonicalSourcePath: "index.mdx",
  canonicalUrl: "index",
  requestedLocale: "en",
  contentLocale: "en",
  isLocaleFallback: false,
  hasExplicitLocale: true,
  availableLocales: ["en", "ru"],
  slug: [],
  href: "/docs",
  headings: [
    { level: 2, title: "Repeated", id: "repeated" },
    { level: 2, title: "Repeated", id: "repeated-2" },
  ],
  meta: {
    title: "Home",
    description: "Docs home",
    group: "General",
    parentItem: "Introduction",
    order: 100,
    status: "published",
    toc: true,
  },
} satisfies DocumentInfo;

const components = createDocumentMdxComponents(document);

function Component({
  component,
  children,
  ...props
}: { component: keyof typeof components; children?: ReactNode } & Record<
  string,
  unknown
>) {
  const Selected = components[component] as ComponentType<
    Record<string, unknown>
  >;
  return <Selected {...props}>{children}</Selected>;
}

beforeEach(() => {
  Object.assign(navigator, {
    clipboard: { writeText: jest.fn().mockResolvedValue(undefined) },
  });
});

it("renders the complete closed custom component vocabulary", () => {
  render(
    <>
      <Component component="Callout" title="Important" variant="warning">
        Read this.
      </Component>
      <Component component="Steps">
        <Component component="Step" title="Prepare">
          Do the work.
        </Component>
      </Component>
      <Component component="Files">
        <Component component="Folder" name="src">
          <Component component="File" name="page.tsx" />
        </Component>
      </Component>
      <Component component="Tabs" defaultValue="preview">
        <Component component="Tab" title="Preview" value="preview">
          Rendered preview
        </Component>
        <Component component="Tab" title="Code" value="code">
          Rendered code
        </Component>
      </Component>
      <Component component="DocumentLinkGrid">
        <Component
          component="DocumentLinkGroup"
          title="General"
          description="Start here"
        >
          <Component
            component="DocumentLinkCard"
            href="/docs/general/quick-start"
            title="Quick start"
          >
            Set up the app.
          </Component>
        </Component>
      </Component>
    </>,
  );

  expect(screen.getByRole("alert")).toHaveTextContent("ImportantRead this.");
  expect(screen.getByText("Prepare")).toBeInTheDocument();
  expect(screen.getByText("src")).toBeInTheDocument();
  expect(screen.getByText("page.tsx")).toBeInTheDocument();
  expect(screen.getByRole("tab", { name: "Preview" })).toHaveAttribute(
    "data-state",
    "active",
  );
  expect(screen.getByRole("link", { name: /Quick start/ })).toHaveAttribute(
    "href",
    "/docs/general/quick-start",
  );
});

it("uses stable duplicate heading IDs and copies heading and code values", async () => {
  const H2 = components.h2 as ComponentType<{ children?: ReactNode }>;
  const Pre = components.pre as ComponentType<{ children?: ReactNode }>;
  render(
    <>
      <H2>Repeated</H2>
      <H2>Repeated</H2>
      <H2>API &amp; UI</H2>
      <Pre>
        <code>{"npm test\n"}</code>
      </Pre>
    </>,
  );

  const headings = screen.getAllByRole("heading", { name: "Repeated" });
  expect(headings[0]).toHaveAttribute("id", "repeated");
  expect(headings[1]).toHaveAttribute("id", "repeated-2");
  expect(screen.getByRole("heading", { name: "API & UI" })).toHaveAttribute(
    "id",
    "api-ui",
  );

  await act(async () => {
    fireEvent.click(screen.getAllByRole("button", { name: "Copy link" })[0]!);
  });
  expect(navigator.clipboard.writeText).toHaveBeenCalledWith(
    expect.stringMatching(/#repeated$/),
  );
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: "Copy code" }));
  });
  expect(navigator.clipboard.writeText).toHaveBeenCalledWith("npm test\n");
});

it("omits generated GFM footnote references from heading IDs", () => {
  const H2 = components.h2 as ComponentType<{ children?: ReactNode }>;
  const Anchor = components.a as ComponentType<{
    children?: ReactNode;
    "data-footnote-ref"?: string;
    href?: string;
  }>;

  render(
    <>
      <H2>
        Heading
        <sup>
          <Anchor data-footnote-ref="" href="#user-content-fn-note">
            1
          </Anchor>
        </sup>
      </H2>
      <H2>
        Heading <sup>detail</sup>
      </H2>
    </>,
  );

  expect(screen.getByRole("heading", { name: "Heading 1" })).toHaveAttribute(
    "id",
    "heading",
  );
  expect(
    screen.getByRole("heading", { name: "Heading detail" }),
  ).toHaveAttribute("id", "heading-detail");
});

it("preserves the generated accessible GFM footnote heading", () => {
  const H2 = components.h2 as ComponentType<{
    children?: ReactNode;
    className?: string;
    id?: string;
  }>;

  render(
    <H2 className="sr-only" id="footnote-label">
      Footnotes
    </H2>,
  );

  const heading = screen.getByRole("heading", { name: "Footnotes" });
  expect(heading).toHaveAttribute("id", "footnote-label");
  expect(heading).toHaveClass("sr-only");
  expect(heading.querySelector("span")).toBeNull();
  expect(screen.queryByRole("button", { name: "Copy link" })).toBeNull();
});

it("keeps authored heading IDs outside generated GFM footnote namespaces", () => {
  const localComponents = createDocumentMdxComponents(document);
  const H2 = localComponents.h2 as ComponentType<{ children?: ReactNode }>;

  const { container } = render(
    <>
      <H2>User content fn note</H2>
      <H2>User content fnref note</H2>
      <a id="user-content-fnref-note" href="#user-content-fn-note">
        1
      </a>
      <ol>
        <li id="user-content-fn-note">Footnote</li>
      </ol>
    </>,
  );

  expect(
    screen.getByRole("heading", { name: "User content fn note" }),
  ).toHaveAttribute("id", "document-heading-user-content-fn-note");
  expect(
    screen.getByRole("heading", { name: "User content fnref note" }),
  ).toHaveAttribute("id", "document-heading-user-content-fnref-note");
  expect(
    container.querySelectorAll('[id="user-content-fn-note"]'),
  ).toHaveLength(1);
  expect(
    container.querySelectorAll('[id="user-content-fnref-note"]'),
  ).toHaveLength(1);
});

it("renders safe links, unavailable documentation targets, images, and GFM tables", () => {
  const Anchor = components.a as ComponentType<{
    children?: ReactNode;
    href?: string;
  }>;
  const Image = components.img as ComponentType<{ alt?: string; src?: string }>;
  const Table = components.table as ComponentType<{ children?: ReactNode }>;
  render(
    <>
      <Anchor href="https://nextjs.org/docs">External</Anchor>
      <Anchor href="/docs">Documentation home</Anchor>
      <Anchor href="/docs/general/quick-start">Quick start</Anchor>
      <Anchor href="/docs/general/quick-start///?from=test#details">
        Quick start with slashes
      </Anchor>
      <Anchor href="/docs/general/quick-start/index">Nested index alias</Anchor>
      <Anchor href="/docs/index/index">Repeated root index alias</Anchor>
      <Anchor href="/docs/%69ndex">Encoded root index alias</Anchor>
      <Anchor href="/docs/general/%71uick-start">Encoded canonical path</Anchor>
      <Anchor href="/docsgeneral/quick-start">
        Malformed documentation prefix
      </Anchor>
      <Anchor href="/docs/private/draft">Unavailable</Anchor>
      <Anchor href="javascript:alert(1)">Unsafe</Anchor>
      <Anchor href=" javascript:alert(1)">Whitespace unsafe</Anchor>
      <Image alt="Template logo" src="/img/branding/template_logo_nb_s.png" />
      <Table>
        <thead>
          <tr>
            <th>Field</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>title</td>
          </tr>
        </tbody>
      </Table>
    </>,
  );

  expect(screen.getByRole("link", { name: "External" })).toHaveAttribute(
    "target",
    "_blank",
  );
  expect(screen.getByRole("link", { name: "External" })).toHaveAttribute(
    "rel",
    "noopener noreferrer",
  );
  expect(
    screen.getByRole("link", { name: "Documentation home" }),
  ).toHaveAttribute("href", "/docs");
  expect(screen.getByRole("link", { name: "Quick start" })).toHaveAttribute(
    "href",
    "/docs/general/quick-start",
  );
  expect(
    screen.getByRole("link", { name: "Quick start with slashes" }),
  ).toHaveAttribute("href", "/docs/general/quick-start///?from=test#details");
  expect(
    screen.queryByRole("link", { name: "Malformed documentation prefix" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("link", { name: "Nested index alias" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("link", { name: "Repeated root index alias" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("link", { name: "Encoded root index alias" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("link", { name: "Encoded canonical path" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("link", { name: "Unavailable" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("link", { name: "Unsafe" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("link", { name: "Whitespace unsafe" }),
  ).not.toBeInTheDocument();
  expect(screen.getByRole("img", { name: "Template logo" })).toHaveClass(
    "max-w-full",
  );
  expect(
    within(screen.getByRole("table")).getByText("title"),
  ).toBeInTheDocument();
});

it("reserves runtime-owned IDs in the runtime heading allocator", () => {
  const H2 = components.h2 as ComponentType<{ children?: ReactNode }>;
  const H3 = components.h3 as ComponentType<{ children?: ReactNode }>;

  render(
    <>
      <H2>Document title</H2>
      <H2>Document title 2</H2>
      <H3>Main content</H3>
      <H2>Footnote label</H2>
      <H2>Document title</H2>
    </>,
  );

  const documentTitles = screen.getAllByRole("heading", {
    name: "Document title",
  });
  expect(documentTitles[0]).toHaveAttribute("id", "document-title-2");
  expect(documentTitles[1]).toHaveAttribute("id", "document-title-3");
  expect(
    screen.getByRole("heading", { name: "Document title 2" }),
  ).toHaveAttribute("id", "document-title-2-2");
  expect(screen.getByRole("heading", { name: "Main content" })).toHaveAttribute(
    "id",
    "main-content-2",
  );
  expect(
    screen.getByRole("heading", { name: "Footnote label" }),
  ).toHaveAttribute("id", "footnote-label-2");
});

it("renders malformed direct document-card href values as unavailable without throwing", () => {
  render(
    <Component
      component="DocumentLinkCard"
      href="http://["
      title="Malformed card"
    />,
  );

  expect(screen.queryByRole("link", { name: /Malformed card/ })).toBeNull();
  expect(
    screen.getByText("Malformed card").closest("[aria-disabled='true']"),
  ).toHaveAttribute("data-document-link-state", "unavailable");
});

it.each(["HTTP://example.com/diagram.png", "HTTPS://example.com/diagram.png"])(
  "renders a remote image with a case-insensitive HTTP scheme: %s",
  (src) => {
    const Image = components.img as ComponentType<{
      alt?: string;
      src?: string;
    }>;

    render(<Image alt="Remote architecture" src={src} />);

    expect(
      screen.getByRole("img", { name: "Remote architecture" }),
    ).toHaveAttribute("src", src);
  },
);
