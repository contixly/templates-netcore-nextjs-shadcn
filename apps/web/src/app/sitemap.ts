import type { MetadataRoute } from "next";

import { getDocumentsRegistry } from "@/src/features/documents/documents-registry";
import { resolveDocumentsLocale } from "@/src/features/documents/documents-routes";
import { resolvePublicOrigin } from "@/src/lib/public-origin";

export default function sitemap(): MetadataRoute.Sitemap {
  const publicOrigin = resolvePublicOrigin();

  const documents = getDocumentsRegistry(
    resolveDocumentsLocale(),
  ).visibleDocuments.map(
    (document) =>
      ({
        url: new URL(document.href, publicOrigin).toString(),
        lastModified: document.meta.editedAt,
        changeFrequency: "weekly",
        priority: document.canonicalUrl === "index" ? 0.8 : 0.6,
      }) satisfies MetadataRoute.Sitemap[number],
  );

  return [
    {
      url: publicOrigin.toString(),
      changeFrequency: "weekly",
      priority: 1,
    },
    ...documents,
  ];
}
