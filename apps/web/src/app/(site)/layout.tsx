import type { ReactNode } from "react";

import { SiteHeader } from "@/src/components/application/site-header";

export default function SiteLayout({
  children,
  organizationSwitcher,
}: Readonly<{
  children: ReactNode;
  organizationSwitcher: ReactNode;
}>) {
  return (
    <>
      <SiteHeader organizationSwitcher={organizationSwitcher} />
      {children}
    </>
  );
}
