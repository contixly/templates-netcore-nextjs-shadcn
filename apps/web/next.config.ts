import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

import { resolveApiBaseUrl } from "./src/lib/api/api-base-url";

const withNextIntl = createNextIntlPlugin("./src/i18n/request.ts");

function readApiProxyTarget(): string | undefined {
  if (!process.env.API_PROXY_TARGET) {
    return undefined;
  }

  const target = resolveApiBaseUrl(process.env.API_PROXY_TARGET);

  if (!target.ok) {
    throw new Error(`API_PROXY_TARGET is invalid (${target.code}).`);
  }

  return target.value;
}

const apiProxyTarget = readApiProxyTarget();

const nextConfig: NextConfig = {
  cacheComponents: true,
  output: "standalone",
  poweredByHeader: false,
  typedRoutes: true,
  transpilePackages: [
    "@formatjs/fast-memoize",
    "@formatjs/icu-messageformat-parser",
    "@formatjs/icu-skeleton-parser",
    "@formatjs/intl-localematcher",
    "intl-messageformat",
    "next-intl",
    "use-intl",
  ],
  async rewrites() {
    return apiProxyTarget
      ? [
          {
            source: "/api/:path*",
            destination: `${apiProxyTarget}/api/:path*`,
          },
        ]
      : [];
  },
};

export default withNextIntl(nextConfig);
