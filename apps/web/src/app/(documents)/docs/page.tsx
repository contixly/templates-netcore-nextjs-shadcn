import { notFound } from "next/navigation";

import { findPublishedDocument } from "@/src/features/documents/documents-registry";
import { resolveDocumentsLocale } from "@/src/features/documents/documents-routes";

export default async function DocumentsHomePage() {
  const document = findPublishedDocument(resolveDocumentsLocale(), "index");

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
