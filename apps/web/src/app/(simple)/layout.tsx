import type { Metadata } from "next";
import type { ReactNode } from "react";

export const metadata: Metadata = {
  robots: { index: false, follow: false },
  alternates: { canonical: null },
  openGraph: { url: null },
};

export default function SimpleLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  return children;
}
