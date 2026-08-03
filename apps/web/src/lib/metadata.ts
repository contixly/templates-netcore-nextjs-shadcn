import type { Metadata } from "next";

import {
  applicationPageCatalog,
  type ApplicationPageId,
} from "@/src/features/application/application-page-catalog";
import { resolveAppLocale, type AppLocale } from "@/src/i18n/config";
import { loadMessages } from "@/src/i18n/messages";
import { resolvePublicOrigin } from "@/src/lib/public-origin";

const openGraphLocales = {
  en: "en_US",
  ru: "ru_RU",
} as const satisfies Record<AppLocale, string>;

export function resolveOpenGraphLocale(locale: AppLocale): string {
  return openGraphLocales[locale];
}

export async function buildApplicationPageMetadata(
  pageId: ApplicationPageId,
  locale = resolveAppLocale(process.env.PUBLIC_DEFAULT_LOCALE),
): Promise<Metadata> {
  const definition = applicationPageCatalog.find(({ id }) => id === pageId);

  if (!definition) {
    throw new Error(`Unknown application page metadata ID: ${pageId}`);
  }

  const messages = await loadMessages(locale);
  const page = messages.application.pages[pageId];
  const publicOrigin = resolvePublicOrigin();
  const title =
    pageId === "home" ? `${messages.common.brand} — ${page.title}` : page.title;
  const canonical = publicOrigin.toString();
  const openGraphImage = new URL("/opengraph-image", publicOrigin).toString();
  const twitterImage = new URL("/twitter-image", publicOrigin).toString();

  return {
    metadataBase: publicOrigin,
    applicationName: messages.common.brand,
    title,
    description: page.description,
    robots: {
      index: definition.indexable,
      follow: definition.indexable,
    },
    alternates: { canonical: definition.indexable ? canonical : null },
    openGraph: {
      type: "website",
      siteName: messages.common.brand,
      title,
      description: page.description,
      locale: resolveOpenGraphLocale(locale),
      url: definition.indexable ? canonical : null,
      images: [
        {
          url: openGraphImage,
          width: 1200,
          height: 630,
          alt: messages.application.landing.title,
        },
      ],
    },
    twitter: {
      card: "summary_large_image",
      title,
      description: page.description,
      images: [twitterImage],
    },
  };
}
