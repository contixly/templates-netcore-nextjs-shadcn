/** @jest-environment node */

import { getDocumentsRegistry } from "@/src/features/documents/documents-registry";
import { resolvePublicOrigin } from "@/src/lib/public-origin";
import HomeOpenGraphImage, {
  contentType,
  size,
} from "@/src/app/(documents)/docs/opengraph-image";
import TwitterImage from "@/src/app/(documents)/docs/twitter-image";
import { generateMetadata as generateHomeMetadata } from "@/src/app/(documents)/docs/page";
import { generateMetadata as generateDocumentMetadata } from "@/src/app/(documents)/docs/[...slug]/page";
import {
  generateStaticParams as generateOgStaticParams,
  GET as getDocumentImage,
} from "@/src/app/(documents)/docs/og/[...slug]/route";

jest.mock("next/og", () => ({
  ImageResponse: class extends Response {
    constructor() {
      super(null, { headers: { "content-type": "image/png" } });
    }
  },
}));

const previousPublicOrigin = process.env.APP_PUBLIC_ORIGIN;
const previousDefaultLocale = process.env.PUBLIC_DEFAULT_LOCALE;

afterEach(() => {
  if (previousPublicOrigin === undefined) {
    delete process.env.APP_PUBLIC_ORIGIN;
  } else {
    process.env.APP_PUBLIC_ORIGIN = previousPublicOrigin;
  }

  if (previousDefaultLocale === undefined) {
    delete process.env.PUBLIC_DEFAULT_LOCALE;
  } else {
    process.env.PUBLIC_DEFAULT_LOCALE = previousDefaultLocale;
  }
});

describe("public origin", () => {
  it("defaults to the local web origin when the deployment value is unset", () => {
    expect(resolvePublicOrigin(undefined).href).toBe("http://localhost:3000/");
  });

  it.each(["http://docs.example.com", "https://docs.example.com:8443"])(
    "accepts the absolute HTTP(S) origin %s",
    (value) => {
      expect(resolvePublicOrigin(value).origin).toBe(value);
    },
  );

  it.each([
    "",
    "   \t\n",
    "docs.example.com",
    "ftp://docs.example.com",
    "https://user:password@docs.example.com",
    "https://docs.example.com/path",
    "https://docs.example.com?preview=1",
    "https://docs.example.com#fragment",
  ])("rejects an unsafe or non-origin deployment value %s", (value) => {
    expect(() => resolvePublicOrigin(value)).toThrow(
      "APP_PUBLIC_ORIGIN must be an absolute HTTP(S) origin",
    );
  });
});

describe("documentation metadata", () => {
  beforeEach(() => {
    process.env.APP_PUBLIC_ORIGIN = "https://docs.example.com";
  });

  it("builds localized, locale-neutral metadata for the docs home", async () => {
    process.env.PUBLIC_DEFAULT_LOCALE = "ru";
    const document = getDocumentsRegistry("ru").visibleDocuments.find(
      ({ canonicalUrl }) => canonicalUrl === "index",
    );

    expect(document).toBeDefined();

    const metadata = await generateHomeMetadata();

    expect(metadata).toMatchObject({
      title: document?.meta.title,
      description: document?.meta.description,
      alternates: { canonical: "https://docs.example.com/docs" },
      openGraph: {
        title: document?.meta.title,
        description: document?.meta.description,
        url: "https://docs.example.com/docs",
      },
      twitter: {
        card: "summary_large_image",
        title: document?.meta.title,
        description: document?.meta.description,
      },
    });
  });

  it("uses the localized article and exact OG route without localizing the canonical URL", async () => {
    process.env.PUBLIC_DEFAULT_LOCALE = "ru";
    const document = getDocumentsRegistry("ru").visibleDocuments.find(
      ({ canonicalUrl }) => canonicalUrl === "api/api-v1",
    );

    expect(document).toBeDefined();

    const metadata = await generateDocumentMetadata({
      params: Promise.resolve({ slug: ["api", "api-v1"] }),
    });

    const imageUrl = "https://docs.example.com/docs/og/api/api-v1?locale=ru";
    expect(metadata).toMatchObject({
      title: document?.meta.title,
      description: document?.meta.description,
      alternates: {
        canonical: "https://docs.example.com/docs/api/api-v1",
      },
      openGraph: {
        title: document?.meta.title,
        description: document?.meta.description,
        url: "https://docs.example.com/docs/api/api-v1",
        images: [{ url: imageUrl, width: 1200, height: 630 }],
      },
      twitter: {
        card: "summary_large_image",
        title: document?.meta.title,
        description: document?.meta.description,
        images: [imageUrl],
      },
    });
  });

  it("generates the docs home Open Graph image and aliases it for Twitter", async () => {
    process.env.PUBLIC_DEFAULT_LOCALE = "en";

    expect(size).toEqual({ width: 1200, height: 630 });
    expect(contentType).toBe("image/png");

    const [openGraphImage, twitterImage] = await Promise.all([
      HomeOpenGraphImage(),
      TwitterImage(),
    ]);

    expect(openGraphImage.headers.get("content-type")).toBe("image/png");
    expect(twitterImage.headers.get("content-type")).toBe("image/png");
  });
});

describe("exact documentation OG route", () => {
  it.each(["en", "ru"])(
    "renders a known localized document as PNG for %s",
    async (locale) => {
      const response = await getDocumentImage(
        new Request(
          `https://docs.example.com/docs/og/api/api-v1?locale=${locale}`,
        ),
        { params: Promise.resolve({ slug: ["api", "api-v1"] }) },
      );

      expect(response.status).toBe(200);
      expect(response.headers.get("content-type")).toBe("image/png");
    },
  );

  it("returns a safe not-found response for an unknown or unpublished slug", async () => {
    const response = await getDocumentImage(
      new Request("https://docs.example.com/docs/og/private/unknown?locale=en"),
      { params: Promise.resolve({ slug: ["private", "unknown"] }) },
    );

    expect(response.status).toBe(404);
    await expect(response.text()).resolves.toBe("");
  });

  it.each(["de", "", "en&locale=ru"])(
    "rejects the invalid locale query %s",
    async (localeQuery) => {
      const response = await getDocumentImage(
        new Request(
          `https://docs.example.com/docs/og/api/api-v1?locale=${localeQuery}`,
        ),
        { params: Promise.resolve({ slug: ["api", "api-v1"] }) },
      );

      expect(response.status).toBe(400);
      await expect(response.text()).resolves.toBe("");
    },
  );

  it("pre-renders one locale-neutral image path per canonical document", () => {
    const params = generateOgStaticParams();

    expect(params).toHaveLength(54);
    expect(params).toContainEqual({ slug: ["index"] });
    expect(params).toContainEqual({ slug: ["api", "api-v1"] });
    expect(new Set(params.map(({ slug }) => slug.join("/"))).size).toBe(54);
  });
});
