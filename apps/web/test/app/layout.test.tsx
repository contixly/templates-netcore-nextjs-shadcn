import type { ReactElement, ReactNode } from "react";

import RootLayout, { generateMetadata } from "@/src/app/layout";
import type { I18nMessages } from "@/src/i18n/messages";

jest.mock("@/src/components/application/app-providers", () => ({
  AppProviders: "app-providers",
}));
jest.mock("@/src/components/application/app-hydration-marker", () => ({
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

  it("generates Russian metadata from the fixed-locale bundle", async () => {
    process.env.PUBLIC_DEFAULT_LOCALE = "ru";
    process.env.APP_PUBLIC_ORIGIN = "https://docs.example.com";

    await expect(generateMetadata()).resolves.toEqual({
      metadataBase: new URL("https://docs.example.com"),
      title: "Состояние системы Template",
      description: "Проверка REST-связи из браузера и с сервера",
    });
  });

  it("wires the Russian fixed-locale config into html and the provider", async () => {
    process.env.PUBLIC_DEFAULT_LOCALE = "ru";

    const html = asElement<{ children: ReactNode; lang: string }>(
      await RootLayout({ children: <span>Content</span> }),
    );
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
    expect(body.type).toBe("body");
    expect(marker.type).toBe("app-hydration-marker");
    expect(provider.type).toBe("app-providers");
    expect(provider.props.locale).toBe("ru");
    expect(provider.props.timeZone).toBe("UTC");
    expect(provider.props.messages.system.page.title).toBe("REST-соединение");
    expect(provider.props.children).toEqual(<span>Content</span>);
  });
});
