import type { Metadata } from "next";
import type { ReactNode } from "react";

import "@/src/app/globals.css";
import { AppHydrationMarker } from "@/src/features/application/ui/app-hydration-marker";
import { AppProviders } from "@/src/features/application/ui/app-providers";
import { loadI18nMessagesConfig } from "@/src/i18n/messages";
import { resolvePublicOrigin } from "@/src/lib/public-origin";

export async function generateMetadata(): Promise<Metadata> {
  const { messages } = await loadI18nMessagesConfig();

  return {
    metadataBase: resolvePublicOrigin(),
    applicationName: messages.common.brand,
  };
}

export default async function RootLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  const { locale, messages, timeZone } = await loadI18nMessagesConfig();

  return (
    <html lang={locale} suppressHydrationWarning>
      <body className="min-h-svh antialiased">
        <AppHydrationMarker />
        <AppProviders locale={locale} messages={messages} timeZone={timeZone}>
          {children}
        </AppProviders>
      </body>
    </html>
  );
}
