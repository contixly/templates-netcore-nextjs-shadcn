import type { ReactNode } from "react";

import { ApplicationHeader } from "@/src/components/application/application-header";
import { SidebarInset, SidebarProvider } from "@/src/components/ui/sidebar";

export function ProtectedApplicationShell({
  children,
  defaultSidebarOpen = false,
  navigation,
}: Readonly<{
  children: ReactNode;
  defaultSidebarOpen?: boolean;
  navigation: ReactNode;
}>) {
  return (
    <SidebarProvider
      className="h-svh min-h-0 overflow-hidden"
      data-application-shell-ready="true"
      defaultOpen={defaultSidebarOpen}
    >
      {navigation}
      <SidebarInset className="h-svh min-h-0 overflow-y-auto">
        <ApplicationHeader />
        <main className="min-w-0 flex-1" id="main-content" tabIndex={-1}>
          {children}
        </main>
      </SidebarInset>
    </SidebarProvider>
  );
}
