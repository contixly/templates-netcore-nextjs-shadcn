import type { Metadata } from "next";
import type { ReactNode } from "react";

import { loadI18nMessagesConfig } from "@/src/i18n/messages";
import { buildApplicationPageMetadata } from "@/src/lib/metadata";

export async function generateMetadata(): Promise<Metadata> {
  const { locale } = await loadI18nMessagesConfig();
  return buildApplicationPageMetadata("home", locale);
}

export default function HomeLayout({ children }: { children: ReactNode }) {
  return <div className="min-h-svh bg-background">{children}</div>;
}
