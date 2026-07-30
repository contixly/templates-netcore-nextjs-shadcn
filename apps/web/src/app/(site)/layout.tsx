import type { ReactNode } from "react";

import { SiteHeader } from "@/src/components/application/site-header";
import { OrganizationSwitcherProvider } from "@/src/components/organizations/organization-switcher-context";

export default function SiteLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  return (
    <OrganizationSwitcherProvider>
      <SiteHeader />
      {children}
    </OrganizationSwitcherProvider>
  );
}
