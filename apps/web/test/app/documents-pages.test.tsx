import { fireEvent, render, screen } from "@testing-library/react";

import DocumentsError from "@/src/app/(documents)/docs/error";
import DocumentsLoading from "@/src/app/(documents)/docs/loading";
import DocumentsHomePage from "@/src/app/(documents)/docs/page";
import DocumentsDocumentPage, {
  generateStaticParams,
} from "@/src/app/(documents)/docs/[...slug]/page";
import { findPublishedDocument } from "@/src/features/documents/documents-registry";
import { buildDocumentStaticParams } from "@/src/features/documents/documents-routes";
import type { DocumentInfo } from "@/src/features/documents/documents-types";
import { notFound } from "next/navigation";

jest.mock("next/navigation", () => ({
  notFound: jest.fn(() => {
    throw new Error("NEXT_NOT_FOUND");
  }),
}));

jest.mock("@/src/features/documents/documents-registry", () => ({
  findPublishedDocument: jest.fn(),
}));

jest.mock("@/src/features/documents/documents-routes", () => ({
  buildDocumentStaticParams: jest.fn(),
  canonicalDocumentUrlFromSlug: (slug: string[]) => slug.join("/"),
  resolveDocumentsLocale: () => "en",
}));

const document = {
  sourcePath: "api/api-v1.en.md",
  canonicalSourcePath: "api/api-v1.md",
  canonicalUrl: "api/api-v1",
  requestedLocale: "en",
  contentLocale: "en",
  isLocaleFallback: false,
  hasExplicitLocale: true,
  availableLocales: ["en", "ru"],
  slug: ["api", "api-v1"],
  href: "/docs/api/api-v1",
  headings: [],
  meta: {
    title: "API v1 reference",
    description: "Generated REST contract reference.",
    group: "API and integrations",
    parentItem: "API reference",
    order: 10,
    status: "published",
    toc: true,
  },
} satisfies DocumentInfo;

const findPublishedDocumentMock = jest.mocked(findPublishedDocument);
const buildDocumentStaticParamsMock = jest.mocked(buildDocumentStaticParams);
const notFoundMock = jest.mocked(notFound);

beforeEach(() => {
  jest.clearAllMocks();
  findPublishedDocumentMock.mockReturnValue(document);
  buildDocumentStaticParamsMock.mockReturnValue([{ slug: ["api", "api-v1"] }]);
});

it("loads the canonical index document for the locale-neutral root", async () => {
  const rootDocument = {
    ...document,
    sourcePath: "index.en.mdx",
    canonicalSourcePath: "index.mdx",
    canonicalUrl: "index",
    slug: [],
    href: "/docs",
    meta: { ...document.meta, title: "Template documentation" },
  } satisfies DocumentInfo;
  findPublishedDocumentMock.mockReturnValue(rootDocument);

  render(await DocumentsHomePage());

  expect(findPublishedDocumentMock).toHaveBeenCalledWith("en", "index");
  expect(
    screen.getByRole("article", { name: "Template documentation" }),
  ).toBeInTheDocument();
});

it("joins the catch-all slug and renders a semantic temporary article", async () => {
  render(
    await DocumentsDocumentPage({
      params: Promise.resolve({ slug: ["api", "api-v1"] }),
    }),
  );

  expect(findPublishedDocumentMock).toHaveBeenCalledWith("en", "api/api-v1");
  expect(
    screen.getByRole("article", { name: "API v1 reference" }),
  ).toHaveTextContent("Generated REST contract reference.");
});

it("pre-renders every production-visible non-root registry path", () => {
  expect(generateStaticParams()).toEqual([{ slug: ["api", "api-v1"] }]);
  expect(buildDocumentStaticParamsMock).toHaveBeenCalledTimes(1);
});

it.each([
  ["root", () => DocumentsHomePage()],
  [
    "catch-all",
    () =>
      DocumentsDocumentPage({
        params: Promise.resolve({ slug: ["private", "draft"] }),
      }),
  ],
])(
  "returns not found for a missing or unpublished %s document",
  async (_, loadPage) => {
    findPublishedDocumentMock.mockReturnValue(undefined);

    await expect(loadPage()).rejects.toThrow("NEXT_NOT_FOUND");
    expect(notFoundMock).toHaveBeenCalledTimes(1);
  },
);

it("provides safe loading and recoverable error boundaries", () => {
  const loading = render(<DocumentsLoading />);
  expect(screen.getByRole("status")).toHaveTextContent("Loading documentation");
  loading.unmount();

  const reset = jest.fn();
  render(
    <DocumentsError
      error={new Error("private-document-error")}
      reset={reset}
    />,
  );

  expect(screen.getByRole("alert")).toHaveTextContent(
    "Documentation is unavailable",
  );
  expect(screen.queryByText("private-document-error")).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Try again" }));
  expect(reset).toHaveBeenCalledTimes(1);
});
