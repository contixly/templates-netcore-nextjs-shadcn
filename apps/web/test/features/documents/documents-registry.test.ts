import {
  findPublishedDocument,
  getDocumentsRegistry,
  importDocument,
} from "@/src/features/documents/documents-registry";
import {
  buildDocumentPageNavigation,
  buildDocumentsSidebarNavigation,
} from "@/src/features/documents/documents-navigation";
import {
  buildDocumentStaticParams,
  resolveDocumentsLocale,
} from "@/src/features/documents/documents-routes";
import { documentModules } from "@/src/features/documents/generated/documents-registry.gen";

jest.mock("@/src/features/documents/generated/documents-registry.gen", () => {
  const actual = jest.requireActual<
    typeof import("@/src/features/documents/generated/documents-registry.gen")
  >("@/src/features/documents/generated/documents-registry.gen");

  return {
    ...actual,
    documentModules: {
      ...actual.documentModules,
      "index.en.mdx": jest.fn(async () => ({ default: () => null })),
    },
  };
});

describe("public documents registry", () => {
  const previousDefaultLocale = process.env.PUBLIC_DEFAULT_LOCALE;

  afterEach(() => {
    if (previousDefaultLocale === undefined) {
      delete process.env.PUBLIC_DEFAULT_LOCALE;
    } else {
      process.env.PUBLIC_DEFAULT_LOCALE = previousDefaultLocale;
    }
  });

  it("selects one production-visible variant for every canonical URL", () => {
    const english = getDocumentsRegistry("en");
    const russian = getDocumentsRegistry("ru");

    expect(english.visibleDocuments).toHaveLength(54);
    expect(russian.visibleDocuments).toHaveLength(54);
    expect(
      new Set(english.visibleDocuments.map(({ canonicalUrl }) => canonicalUrl))
        .size,
    ).toBe(54);
    expect(findPublishedDocument("ru", "workspace")?.contentLocale).toBe("ru");
    expect(findPublishedDocument("en", "workspace")?.contentLocale).toBe("en");
  });

  it("uses the fixed deployment locale and safely falls back to English", () => {
    process.env.PUBLIC_DEFAULT_LOCALE = "ru";
    expect(resolveDocumentsLocale()).toBe("ru");

    process.env.PUBLIC_DEFAULT_LOCALE = "de";
    expect(resolveDocumentsLocale()).toBe("en");

    process.env.PUBLIC_DEFAULT_LOCALE = "";
    expect(resolveDocumentsLocale()).toBe("en");
  });

  it("builds canonical locale-neutral static parameters", () => {
    const parameters = buildDocumentStaticParams();

    expect(parameters).toHaveLength(53);
    expect(parameters).toContainEqual({ slug: ["api", "api-v1"] });
    expect(parameters).not.toContainEqual({ slug: ["index"] });
    expect(
      parameters.some(({ slug }) =>
        slug.some((segment) => /\.(?:en|ru)$/u.test(segment)),
      ),
    ).toBe(false);
  });

  it("preserves generated group, parent, document, and previous-next order", () => {
    const documents = getDocumentsRegistry("en").visibleDocuments;
    const sidebar = buildDocumentsSidebarNavigation(documents);
    const apiNavigation = buildDocumentPageNavigation(documents, "api/api-v1");

    expect(sidebar.slice(0, 3).map(({ label }) => label)).toEqual([
      "General",
      "Account",
      "Workspace",
    ]);
    expect(sidebar[0]?.parents.map(({ label }) => label)).toEqual([
      "Introduction",
      "Getting started",
      "Glossary",
    ]);
    expect(apiNavigation.previous).toMatchObject({
      canonicalUrl: "api/api-keys",
      href: "/docs/api/api-keys",
    });
    expect(apiNavigation.next).toMatchObject({
      canonicalUrl: "api/permissions-rate-limits",
      href: "/docs/api/permissions-rate-limits",
    });
  });

  it("imports content only through the generated module map", async () => {
    const document = findPublishedDocument("en", "index");

    expect(document).toBeDefined();
    await expect(importDocument(document!)).resolves.toEqual(
      expect.objectContaining({ default: expect.any(Function) }),
    );
    expect(documentModules["index.en.mdx"]).toHaveBeenCalledTimes(1);
  });
});
