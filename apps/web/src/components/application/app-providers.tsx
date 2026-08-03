"use client";

import { NextIntlClientProvider } from "next-intl";
import { ThemeProvider } from "next-themes";
import type { ReactNode } from "react";

import type { AppLocale } from "@/src/i18n/config";
import type { I18nMessages } from "@/src/i18n/messages";
import { TooltipProvider } from "@/src/components/ui/tooltip";

export function AppProviders({
  children,
  locale,
  messages,
  timeZone,
}: Readonly<{
  children: ReactNode;
  locale: AppLocale;
  messages: I18nMessages;
  timeZone: string;
}>) {
  return (
    <ThemeProvider
      attribute="class"
      defaultTheme="system"
      disableTransitionOnChange
      enableSystem
      storageKey="template.theme"
    >
      <NextIntlClientProvider
        locale={locale}
        messages={messages}
        timeZone={timeZone}
      >
        <TooltipProvider>{children}</TooltipProvider>
      </NextIntlClientProvider>
    </ThemeProvider>
  );
}
