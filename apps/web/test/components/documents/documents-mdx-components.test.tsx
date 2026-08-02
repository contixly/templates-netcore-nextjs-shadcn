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
      <Pre>
        <code>{"npm test\n"}</code>
      </Pre>
    </>,
  );

  const headings = screen.getAllByRole("heading", { name: "Repeated" });
  expect(headings[0]).toHaveAttribute("id", "repeated");
  expect(headings[1]).toHaveAttribute("id", "repeated-2");

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
      <Anchor href="/docsgeneral/quick-start">
        Malformed documentation prefix
      </Anchor>
      <Anchor href="/docs/private/draft">Unavailable</Anchor>
      <Anchor href="javascript:alert(1)">Unsafe</Anchor>
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
    screen.queryByRole("link", { name: "Malformed documentation prefix" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("link", { name: "Unavailable" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("link", { name: "Unsafe" }),
  ).not.toBeInTheDocument();
  expect(screen.getByRole("img", { name: "Template logo" })).toHaveClass(
    "max-w-full",
  );
  expect(
    within(screen.getByRole("table")).getByText("title"),
  ).toBeInTheDocument();
});
