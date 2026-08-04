import type { MetadataRoute } from "next";

import { resolvePublicOrigin } from "@/src/lib/public-origin";

export default function robots(): MetadataRoute.Robots {
  const publicOrigin = resolvePublicOrigin();

  return {
    rules: {
      userAgent: "*",
      allow: ["/", "/docs/"],
      disallow: [
        "/api/",
        "/auth/",
        "/dashboard",
        "/invite/",
        "/user",
        "/w/",
        "/welcome",
        "/workspaces",
      ],
    },
    sitemap: new URL("/sitemap.xml", publicOrigin).toString(),
    host: publicOrigin.origin,
  };
}
