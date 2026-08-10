import type { ReactElement, ReactNode } from "react";

import { generateMetadata as generateHomeMetadata } from "@/src/app/(public)/(home)/layout";
import { metadata as protectedMetadata } from "@/src/app/(protected)/layout";
import { metadata as simpleMetadata } from "@/src/app/(simple)/layout";
import RootLayout, { generateMetadata } from "@/src/app/layout";
import type { I18nMessages } from "@/src/i18n/messages";

jest.mock("@/src/features/application/ui/app-providers", () => ({
  AppProviders: "app-providers",
}));
jest.mock("@/src/features/application/ui/app-hydration-marker", () => ({
  AppHydrationMarker: "app-hydration-marker",
}));

function asElement<Props extends object>(node: ReactNode): ReactElement<Props> {
  if (typeof node !== "object" || node === null || !("type" in node)) {
    throw new Error("Expected a React element");
  }

  return node as ReactElement<Props>;
}

describe("RootLayout", () => {
  const originalLocale = process.env.PUBLIC_DEFAULT_LOCALE;
  const originalPublicOrigin = process.env.APP_PUBLIC_ORIGIN;

  afterEach(() => {
    if (originalLocale === undefined) {
      delete process.env.PUBLIC_DEFAULT_LOCALE;
    } else {
      process.env.PUBLIC_DEFAULT_LOCALE = originalLocale;
    }

    if (originalPublicOrigin === undefined) {
      delete process.env.APP_PUBLIC_ORIGIN;
    } else {
      process.env.APP_PUBLIC_ORIGIN = originalPublicOrigin;
    }
  });

  it("keeps root metadata neutral so landing metadata cannot leak into child routes", async () => {
    process.env.PUBLIC_DEFAULT_LOCALE = "ru";
    process.env.APP_PUBLIC_ORIGIN = "https://docs.example.com";

    await expect(generateMetadata()).resolves.toEqual({
      metadataBase: new URL("https://docs.example.com"),
      applicationName: "Template",
    });
  });

  it("scopes localized indexable metadata to the public home layout", async () => {
    process.env.PUBLIC_DEFAULT_LOCALE = "ru";
    process.env.APP_PUBLIC_ORIGIN = "https://docs.example.com";

    await expect(generateHomeMetadata()).resolves.toMatchObject({
      metadataBase: new URL("https://docs.example.com"),
      title: expect.stringContaining("Template"),
      description: "Начните с безопасной основы приложения.",
      alternates: { canonical: "https://docs.example.com/" },
      robots: { index: true, follow: true },
      openGraph: {
        locale: "ru_RU",
        url: "https://docs.example.com/",
      },
    });
  });

  it("makes protected and authentication groups explicitly non-indexable with no canonical or OG URL", () => {
    const expected = {
      robots: { index: false, follow: false },
      alternates: { canonical: null },
      openGraph: { url: null },
    };

    expect(protectedMetadata).toEqual(expected);
    expect(simpleMetadata).toEqual(expected);
  });

  it("wires the Russian fixed-locale config into html and the provider", async () => {
    process.env.PUBLIC_DEFAULT_LOCALE = "ru";

    const html = asElement<{
      children: ReactNode;
      lang: string;
      suppressHydrationWarning: boolean;
    }>(await RootLayout({ children: <span>Content</span> }));
    const body = asElement<{ children: ReactNode }>(html.props.children);
    const [markerNode, providerNode] = body.props.children as ReactNode[];
    const marker = asElement(markerNode);
    const provider = asElement<{
      children: ReactNode;
      locale: string;
      messages: I18nMessages;
      timeZone: string;
    }>(providerNode);

    expect(html.type).toBe("html");
    expect(html.props.lang).toBe("ru");
    expect(html.props.suppressHydrationWarning).toBe(true);
    expect(body.type).toBe("body");
    expect(marker.type).toBe("app-hydration-marker");
    expect(provider.type).toBe("app-providers");
    expect(provider.props.locale).toBe("ru");
    expect(provider.props.timeZone).toBe("UTC");
    expect(provider.props.messages.system.page.title).toBe("REST-соединение");
    expect(provider.props.children).toEqual(<span>Content</span>);
  });
});
