import { notFound } from "next/navigation";

import { findPublishedDocument } from "@/src/features/documents/documents-registry";
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

  return (
    <article aria-labelledby="document-title">
      <header>
        <h1 id="document-title">{document.meta.title}</h1>
        <p>{document.meta.description}</p>
      </header>
    </article>
  );
}
