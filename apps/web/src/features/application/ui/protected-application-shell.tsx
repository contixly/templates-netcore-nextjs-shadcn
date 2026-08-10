import type { CSSProperties, ReactNode } from "react";

import { ApplicationHeader } from "@/src/features/application/ui/application-header";
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
      style={
        {
          "--header-height": "calc(var(--spacing) * 12)",
          "--sidebar-width": "calc(var(--spacing) * 72)",
        } as CSSProperties
      }
    >
      {navigation}
      <SidebarInset className="h-svh max-h-svh min-h-0 overflow-y-auto">
        <ApplicationHeader />
        <main
          className="flex min-h-[calc(100svh-var(--header-height))] w-full max-w-[2048px] min-w-0 flex-1 flex-col border-b pb-4 lg:border-b-0 lg:pb-0"
          id="main-content"
          tabIndex={-1}
        >
          {children}
        </main>
      </SidebarInset>
    </SidebarProvider>
  );
}
