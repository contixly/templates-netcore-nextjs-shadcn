import type { Metadata } from "next";
import { NextIntlClientProvider } from "next-intl";
import type { ReactNode } from "react";

import "@/src/app/globals.css";
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
    <html lang={locale}>
      <body>
        <NextIntlClientProvider
          locale={locale}
          messages={messages}
          timeZone={timeZone}
        >
          {children}
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
