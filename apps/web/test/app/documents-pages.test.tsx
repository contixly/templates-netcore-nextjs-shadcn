import { fireEvent, render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";

import DocumentsError from "@/src/app/(documents)/docs/error";
import DocumentsLoading from "@/src/app/(documents)/docs/loading";
import DocumentsHomePage from "@/src/app/(documents)/docs/page";
import DocumentsDocumentPage, {
  generateMetadata as generateDocumentMetadata,
  generateStaticParams,
} from "@/src/app/(documents)/docs/[...slug]/page";
import {
  findPublishedDocument,
  importDocument,
} from "@/src/features/documents/documents-registry";
import { buildDocumentStaticParams } from "@/src/features/documents/documents-routes";
import type { DocumentInfo } from "@/src/features/documents/documents-types";
import documentsEn from "@/src/messages/documents.en.json";
import documentsRu from "@/src/messages/documents.ru.json";
import { renderWithMessages } from "@/test/support/render";
import { notFound, permanentRedirect } from "next/navigation";

const mockGetTranslations = jest.fn();

jest.mock("next-intl/server", () => ({
  getTranslations: (...args: unknown[]) => mockGetTranslations(...args),
}));

jest.mock("next/navigation", () => ({
  notFound: jest.fn(() => {
    throw new Error("NEXT_NOT_FOUND");
  }),
  permanentRedirect: jest.fn(() => {
    throw new Error("NEXT_REDIRECT");
  }),
}));

jest.mock("@/src/features/documents/documents-registry", () => ({
  findPublishedDocument: jest.fn(),
  importDocument: jest.fn(),
}));

jest.mock("@/src/features/documents/documents-routes", () => ({
  buildDocumentStaticParams: jest.fn(),
  canonicalDocumentUrlFromSlug: (slug: string[]) => slug.join("/"),
  documentsRoutes: {
    root: "/docs",
    document: (canonicalUrl: string) => `/docs/${canonicalUrl}`,
  },
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
const importDocumentMock = jest.mocked(importDocument);
const buildDocumentStaticParamsMock = jest.mocked(buildDocumentStaticParams);
const notFoundMock = jest.mocked(notFound);
const permanentRedirectMock = jest.mocked(permanentRedirect);

type DocumentsBoundaryMessages = {
  loading: string;
  errorTitle: string;
  errorDescription: string;
  retry: string;
};

const expectedBoundaryMessages = {
  en: {
    loading: "Loading documentation",
    errorTitle: "Documentation is unavailable",
    errorDescription: "The requested document could not be rendered safely.",
    retry: "Try again",
  },
  ru: {
    loading: "Загрузка документации",
    errorTitle: "Документация недоступна",
    errorDescription: "Не удалось безопасно отобразить запрошенный документ.",
    retry: "Повторить",
  },
} satisfies Record<"en" | "ru", DocumentsBoundaryMessages>;

beforeEach(() => {
  jest.clearAllMocks();
  findPublishedDocumentMock.mockReturnValue(document);
  importDocumentMock.mockResolvedValue({ default: () => <p>Imported MDX</p> });
  buildDocumentStaticParamsMock.mockReturnValue([{ slug: ["api", "api-v1"] }]);
  mockGetTranslations.mockResolvedValue(
    (key: keyof DocumentsBoundaryMessages) => expectedBoundaryMessages.en[key],
  );
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

  renderWithMessages(await DocumentsHomePage());

  expect(findPublishedDocumentMock).toHaveBeenCalledWith("en", "index");
  expect(
    screen.getByRole("article", { name: "Template documentation" }),
  ).toBeInTheDocument();
});

it("joins the catch-all slug and renders the imported article", async () => {
  renderWithMessages(
    await DocumentsDocumentPage({
      params: Promise.resolve({ slug: ["api", "api-v1"] }),
    }),
  );

  expect(findPublishedDocumentMock).toHaveBeenCalledWith("en", "api/api-v1");
  expect(
    screen.getByRole("article", { name: "API v1 reference" }),
  ).toHaveTextContent("Generated REST contract reference.");
});

it.each([
  [
    "page",
    () =>
      DocumentsDocumentPage({
        params: Promise.resolve({ slug: ["index"] }),
      }),
  ],
  [
    "metadata",
    () =>
      generateDocumentMetadata({
        params: Promise.resolve({ slug: ["index"] }),
      }),
  ],
])(
  "permanently redirects the catch-all docs index %s to /docs",
  async (_, resolveRoute) => {
    await expect(resolveRoute()).rejects.toThrow("NEXT_REDIRECT");
    expect(permanentRedirectMock).toHaveBeenCalledWith("/docs");
    expect(findPublishedDocumentMock).not.toHaveBeenCalled();
  },
);

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

it.each([
  ["en", documentsEn],
  ["ru", documentsRu],
] as const)(
  "provides safe localized %s loading and recoverable error boundaries",
  async (locale, messages) => {
    const registeredBoundary = (
      messages as typeof messages & { boundary?: DocumentsBoundaryMessages }
    ).boundary;
    const expected = expectedBoundaryMessages[locale];

    expect(registeredBoundary).toEqual(expected);
    mockGetTranslations.mockResolvedValue(
      (key: keyof DocumentsBoundaryMessages) => registeredBoundary?.[key],
    );
    const loading = render(await DocumentsLoading());
    expect(screen.getByRole("status")).toHaveTextContent(expected.loading);
    loading.unmount();

    const reset = jest.fn();
    render(
      <NextIntlClientProvider
        locale={locale}
        messages={{ documents: messages }}
      >
        <DocumentsError
          error={new Error("private-document-error")}
          reset={reset}
        />
      </NextIntlClientProvider>,
    );

    expect(screen.getByRole("alert")).toHaveTextContent(expected.errorTitle);
    expect(screen.getByRole("alert")).toHaveTextContent(
      expected.errorDescription,
    );
    expect(
      screen.queryByText("private-document-error"),
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: expected.retry }));
    expect(reset).toHaveBeenCalledTimes(1);
  },
);
