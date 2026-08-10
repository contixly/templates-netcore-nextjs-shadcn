import type { ReactNode } from "react";

import { DocumentsShell } from "@/src/features/documents/ui/documents-shell";
import {
  buildDocumentPageNavigation,
  buildDocumentsSidebarNavigation,
} from "@/src/features/documents/documents-navigation";
import { getDocumentsRegistry } from "@/src/features/documents/documents-registry";
import { resolveDocumentsLocale } from "@/src/features/documents/documents-routes";

export default function DocumentsLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  const documents = getDocumentsRegistry(
    resolveDocumentsLocale(),
  ).visibleDocuments;
  const navigation = buildDocumentsSidebarNavigation(documents);
  const pageNavigationByHref = Object.fromEntries(
    documents.map((document) => [
      document.href,
      buildDocumentPageNavigation(documents, document.canonicalUrl),
    ]),
  );

  return (
    <DocumentsShell
      navigation={navigation}
      pageNavigationByHref={pageNavigationByHref}
    >
      {children}
    </DocumentsShell>
  );
}
