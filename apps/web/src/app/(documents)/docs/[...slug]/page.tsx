import { notFound } from "next/navigation";

import { DocumentsPage } from "@/src/components/documents/documents-page";
import {
  findPublishedDocument,
  importDocument,
} from "@/src/features/documents/documents-registry";
import {
  buildDocumentStaticParams,
  canonicalDocumentUrlFromSlug,
  resolveDocumentsLocale,
} from "@/src/features/documents/documents-routes";

export function generateStaticParams() {
  return buildDocumentStaticParams();
}

export default async function DocumentsDocumentPage({
  params,
}: {
  params: Promise<{ slug: string[] }>;
}) {
  const { slug } = await params;
  const canonicalUrl = canonicalDocumentUrlFromSlug(slug);
  const document = findPublishedDocument(
    resolveDocumentsLocale(),
    canonicalUrl,
  );

  if (!document) {
    notFound();
  }

  const { default: Content } = await importDocument(document);
  return <DocumentsPage Content={Content} document={document} />;
}
