import { getDocumentsRegistry } from "@/src/features/documents/documents-registry";
import sitemap from "@/src/app/sitemap";

const previousPublicOrigin = process.env.APP_PUBLIC_ORIGIN;

afterEach(() => {
  if (previousPublicOrigin === undefined) {
    delete process.env.APP_PUBLIC_ORIGIN;
  } else {
    process.env.APP_PUBLIC_ORIGIN = previousPublicOrigin;
  }
});

it("emits every visible canonical documentation URL exactly once", () => {
  process.env.APP_PUBLIC_ORIGIN = "http://localhost:3000";

  const entries = sitemap();
  const urls = entries.map(({ url }) => url);

  expect(entries).toHaveLength(55);
  expect(new Set(urls).size).toBe(55);
  expect(urls[0]).toBe("http://localhost:3000/");
  expect(urls).toContain("http://localhost:3000/docs");
  expect(urls).toContain("http://localhost:3000/docs/api/api-v1");
  expect(urls.some((url) => /\.(en|ru)(?:\/|$)/u.test(url))).toBe(false);
  expect(
    urls.some((url) =>
      /^\/(?:api|auth|dashboard|invite|user|w|welcome|workspaces)(?:\/|$)/u.test(
        new URL(url).pathname,
      ),
    ),
  ).toBe(false);
});

it("uses registry edit dates and stable documentation crawl policy", () => {
  process.env.APP_PUBLIC_ORIGIN = "https://docs.example.com";
  const entries = sitemap();
  const documentsByUrl = new Map(
    getDocumentsRegistry("en").visibleDocuments.map((document) => [
      document.href,
      document,
    ]),
  );

  expect(entries[0]).toEqual({
    url: "https://docs.example.com/",
    changeFrequency: "weekly",
    priority: 1,
  });

  for (const entry of entries.slice(1)) {
    const path = new URL(entry.url).pathname;
    const document = documentsByUrl.get(path);

    expect(document).toBeDefined();
    expect(entry.lastModified).toBe(document?.meta.editedAt);
    expect(entry.changeFrequency).toBe("weekly");
    expect(entry.priority).toBe(path === "/docs" ? 0.8 : 0.6);
  }
});
