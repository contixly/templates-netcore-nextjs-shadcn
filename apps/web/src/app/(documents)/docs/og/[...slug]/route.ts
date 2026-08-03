import {
  getDocumentsRegistry,
  findPublishedDocument,
} from "@/src/features/documents/documents-registry";
import {
  canonicalDocumentUrlFromSlug,
  resolveDocumentsLocale,
} from "@/src/features/documents/documents-routes";
import { isAppLocale, type AppLocale } from "@/src/i18n/config";
import { createDocumentSocialImage } from "@/src/lib/documents-social-image";

function readImageLocale(request: Request): AppLocale | undefined | null {
  const values = new URL(request.url).searchParams.getAll("locale");

  if (values.length === 0) {
    return resolveDocumentsLocale();
  }
  if (values.length !== 1 || !isAppLocale(values[0])) {
    return null;
  }

  return values[0];
}

export function generateStaticParams(): Array<{ slug: string[] }> {
  return getDocumentsRegistry(resolveDocumentsLocale()).visibleDocuments.map(
    (document) => ({
      slug: document.canonicalUrl === "index" ? ["index"] : [...document.slug],
    }),
  );
}

export async function GET(
  request: Request,
  { params }: { params: Promise<{ slug: string[] }> },
): Promise<Response> {
  const locale = readImageLocale(request);
  if (!locale) {
    return new Response(null, { status: 400 });
  }

  const { slug } = await params;
  const document = findPublishedDocument(
    locale,
    canonicalDocumentUrlFromSlug(slug),
  );

  if (!document) {
    return new Response(null, { status: 404 });
  }

  return createDocumentSocialImage(document);
}
