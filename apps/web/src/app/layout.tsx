import type { Metadata } from "next";
import type { ReactNode } from "react";

import "@/src/app/globals.css";

export const metadata: Metadata = {
  title: "Template",
  description: "Next.js UI foundation",
};

export default function RootLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
