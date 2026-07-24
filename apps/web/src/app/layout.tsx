import type { Metadata } from "next";
import type { ReactNode } from "react";

import "@/src/app/globals.css";
import { AppProviders } from "@/src/components/application/app-providers";
import { loadI18nMessagesConfig } from "@/src/i18n/messages";

export async function generateMetadata(): Promise<Metadata> {
  const { messages } = await loadI18nMessagesConfig();

  return {
    title: messages.system.metadata.title,
    description: messages.system.metadata.description,
  };
}

export default async function RootLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  const { locale, messages, timeZone } = await loadI18nMessagesConfig();

  return (
    <html lang={locale} suppressHydrationWarning>
      <body>
        <AppProviders locale={locale} messages={messages} timeZone={timeZone}>
          {children}
        </AppProviders>
      </body>
    </html>
  );
}
