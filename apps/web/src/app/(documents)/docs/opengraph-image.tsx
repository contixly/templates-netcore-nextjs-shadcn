import { findPublishedDocument } from "@/src/features/documents/documents-registry";
import { resolveDocumentsLocale } from "@/src/features/documents/documents-routes";
import {
  createDocumentSocialImage,
  DOCUMENT_SOCIAL_IMAGE_SIZE,
} from "@/src/lib/documents-social-image";

export const size = DOCUMENT_SOCIAL_IMAGE_SIZE;
export const contentType = "image/png";

export default function OpenGraphImage() {
  const document = findPublishedDocument(resolveDocumentsLocale(), "index");

  if (!document) {
    throw new Error("The documentation home image source is unavailable.");
  }

  return createDocumentSocialImage(document);
}
