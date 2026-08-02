"use client";

import { IconX } from "@tabler/icons-react";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import type { ReactNode } from "react";
import { useState } from "react";

import {
  DocumentsBreadcrumb,
  findDocumentBreadcrumbContext,
} from "@/src/components/documents/documents-breadcrumb";
import { DocumentsHeader } from "@/src/components/documents/documents-header";
import { DocumentsPageNavigation } from "@/src/components/documents/documents-page-navigation";
import { DocumentsSidebar } from "@/src/components/documents/documents-sidebar";
import { Button } from "@/src/components/ui/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/src/components/ui/dialog";
import type {
  DocumentPageNavigation,
  DocumentsSidebarGroup,
} from "@/src/features/documents/documents-types";

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
  const t = useTranslations("documents");
  const [mobileOpen, setMobileOpen] = useState(false);
  const current = findDocumentBreadcrumbContext(navigation, pathname);

  return (
    <div className="min-h-screen bg-background">
      <DocumentsHeader onOpenNavigation={() => setMobileOpen(true)} />
      <Dialog onOpenChange={setMobileOpen} open={mobileOpen}>
        <DialogContent
          aria-describedby={undefined}
          className="top-0 left-0 h-dvh max-w-sm translate-x-0 translate-y-0 overflow-y-auto p-0 sm:max-w-sm"
          showCloseButton={false}
        >
          <DialogHeader className="sticky top-0 flex-row items-center border-b bg-background p-4">
            <DialogTitle>{t("sidebar.title")}</DialogTitle>
            <DialogClose asChild>
              <Button
                aria-label={t("sidebar.close")}
                className="ml-auto"
                size="icon"
                variant="ghost"
              >
                <IconX aria-hidden="true" />
              </Button>
            </DialogClose>
          </DialogHeader>
          <div className="p-4">
            <DocumentsSidebar
              currentHref={pathname}
              navigation={navigation}
              onNavigate={() => setMobileOpen(false)}
            />
          </div>
        </DialogContent>
      </Dialog>

      <div className="mx-auto grid max-w-7xl grid-cols-1 lg:grid-cols-[16rem_minmax(0,1fr)]">
        <aside className="hidden border-r px-4 py-8 lg:block">
          <DocumentsSidebar currentHref={pathname} navigation={navigation} />
        </aside>
        <main
          className="min-w-0 px-4 py-8 sm:px-8 lg:px-12"
          id="main-content"
          tabIndex={-1}
        >
          <DocumentsBreadcrumb current={current} />
          <div className="mx-auto mt-8 max-w-3xl">
            {children}
            <DocumentsPageNavigation
              navigation={pageNavigationByHref[pathname]}
            />
          </div>
        </main>
      </div>
    </div>
  );
}
