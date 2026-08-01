import { Suspense, type ReactNode } from "react";

import { AuthenticatedAccountNavigation } from "@/src/components/account/authenticated-account-navigation";
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
      <SiteHeader
        accountNavigation={
          <Suspense fallback={null}>
            <AuthenticatedAccountNavigation />
          </Suspense>
        }
        organizationSwitcher={organizationSwitcher}
      />
      {children}
    </>
  );
}
