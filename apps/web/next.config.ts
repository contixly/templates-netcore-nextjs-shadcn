import type { NextConfig } from "next";

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
};

export default nextConfig;
