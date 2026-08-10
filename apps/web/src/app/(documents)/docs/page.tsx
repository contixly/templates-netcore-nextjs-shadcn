import type { Metadata } from "next";
import { notFound } from "next/navigation";

import { DocumentsPage } from "@/src/features/documents/ui/documents-page";
import {
  findPublishedDocument,
  importDocument,
} from "@/src/features/documents/documents-registry";
import {
  documentsRoutes,
  resolveDocumentsLocale,
} from "@/src/features/documents/documents-routes";
import { resolvePublicOrigin } from "@/src/lib/public-origin";

export async function generateMetadata(): Promise<Metadata> {
  const locale = resolveDocumentsLocale();
  const document = findPublishedDocument(locale, "index");

  if (!document) {
    notFound();
  }

  const publicOrigin = resolvePublicOrigin();
  const canonicalUrl = new URL(documentsRoutes.root, publicOrigin).toString();

  return {
    title: document.meta.title,
    description: document.meta.description,
    alternates: { canonical: canonicalUrl },
    openGraph: {
      type: "website",
      title: document.meta.title,
      description: document.meta.description,
      url: canonicalUrl,
    },
    twitter: {
      card: "summary_large_image",
      title: document.meta.title,
      description: document.meta.description,
    },
  };
}

export default async function DocumentsHomePage() {
  const document = findPublishedDocument(resolveDocumentsLocale(), "index");

  if (!document) {
    notFound();
  }

  const { default: Content } = await importDocument(document);
  return <DocumentsPage Content={Content} document={document} />;
}
