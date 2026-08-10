"use client";

import { usePathname } from "next/navigation";
import type { CSSProperties, ReactNode } from "react";

import { SidebarProvider } from "@/src/components/ui/sidebar";
import { findDocumentBreadcrumbContext } from "@/src/features/documents/ui/documents-breadcrumb";
import { DocumentsHeader } from "@/src/features/documents/ui/documents-header";
import { DocumentsPageNavigation } from "@/src/features/documents/ui/documents-page-navigation";
import { DocumentsSidebar } from "@/src/features/documents/ui/documents-sidebar";
import { documentsRoutes } from "@/src/features/documents/documents-routes";
import type {
  DocumentPageNavigation,
  DocumentsSidebarGroup,
} from "@/src/features/documents/documents-types";

const documentsSidebarStyle = {
  "--sidebar-width": "24rem",
} as CSSProperties;

export function DocumentsShell({
  children,
  navigation,
  pageNavigationByHref,
}: Readonly<{
  children: ReactNode;
  navigation: DocumentsSidebarGroup[];
  pageNavigationByHref: Record<string, DocumentPageNavigation>;
}>) {
  const pathname = usePathname();
  const current =
    pathname === documentsRoutes.root
      ? undefined
      : findDocumentBreadcrumbContext(navigation, pathname);
  const pageNavigation = pageNavigationByHref[pathname];

  return (
    <SidebarProvider defaultOpen style={documentsSidebarStyle}>
      <DocumentsSidebar currentHref={pathname} navigation={navigation} />
      <main
        className="relative flex h-svh min-w-0 flex-1 flex-col overflow-y-auto bg-background"
        data-documents-scroll-container
        data-slot="sidebar-inset"
        id="main-content"
        tabIndex={-1}
      >
        <DocumentsHeader current={current} onOpenNavigation={() => undefined} />
        <div className="relative min-w-0 flex-1">
          <DocumentsPageNavigation
            navigation={pageNavigation}
            placement="top"
          />
          {children}
          <div className="mx-auto grid w-full max-w-[1400px] grid-cols-1 px-4 pb-[calc(8rem+env(safe-area-inset-bottom))] sm:px-6 lg:px-10 xl:grid-cols-[minmax(0,1fr)_18rem] xl:gap-12 xl:px-12">
            <DocumentsPageNavigation navigation={pageNavigation} />
          </div>
        </div>
      </main>
    </SidebarProvider>
  );
}
