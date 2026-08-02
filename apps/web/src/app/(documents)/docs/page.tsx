import { notFound } from "next/navigation";

import { DocumentsPage } from "@/src/components/documents/documents-page";
import {
  findPublishedDocument,
  importDocument,
} from "@/src/features/documents/documents-registry";
import { resolveDocumentsLocale } from "@/src/features/documents/documents-routes";

export default async function DocumentsHomePage() {
  const document = findPublishedDocument(resolveDocumentsLocale(), "index");

  if (!document) {
    notFound();
  }

  const { default: Content } = await importDocument(document);
  return <DocumentsPage Content={Content} document={document} />;
}
