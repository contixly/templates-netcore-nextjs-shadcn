import type { Metadata } from "next";
import { notFound } from "next/navigation";

import { DocumentsPage } from "@/src/components/documents/documents-page";
import {
  findPublishedDocument,
  importDocument,
} from "@/src/features/documents/documents-registry";
import {
  buildDocumentStaticParams,
  canonicalDocumentUrlFromSlug,
  documentsRoutes,
  resolveDocumentsLocale,
} from "@/src/features/documents/documents-routes";
import { resolvePublicOrigin } from "@/src/lib/public-origin";

export function generateStaticParams() {
  return buildDocumentStaticParams();
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ slug: string[] }>;
}): Promise<Metadata> {
  const { slug } = await params;
  const canonicalDocumentUrl = canonicalDocumentUrlFromSlug(slug);
  const locale = resolveDocumentsLocale();
  const document = findPublishedDocument(locale, canonicalDocumentUrl);

  if (!document) {
    notFound();
  }

  const publicOrigin = resolvePublicOrigin();
  const canonicalUrl = new URL(
    documentsRoutes.document(document.canonicalUrl),
    publicOrigin,
  ).toString();
  const imageUrl = new URL(`/docs/og/${document.canonicalUrl}`, publicOrigin);
  imageUrl.searchParams.set("locale", locale);

  return {
    title: document.meta.title,
    description: document.meta.description,
    alternates: { canonical: canonicalUrl },
    openGraph: {
      type: "article",
      title: document.meta.title,
      description: document.meta.description,
      url: canonicalUrl,
      images: [
        {
          url: imageUrl.toString(),
          width: 1200,
          height: 630,
          alt: document.meta.title,
        },
      ],
    },
    twitter: {
      card: "summary_large_image",
      title: document.meta.title,
      description: document.meta.description,
      images: [imageUrl.toString()],
    },
  };
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
