import type { ReactNode } from "react";

import { SiteHeader } from "@/src/components/application/site-header";

export default function SiteLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  return (
    <>
      <SiteHeader />
      {children}
    </>
  );
}
