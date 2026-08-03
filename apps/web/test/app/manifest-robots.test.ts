/** @jest-environment node */

import { readFileSync } from "node:fs";
import { join } from "node:path";

import manifest from "@/src/app/manifest";
import robots from "@/src/app/robots";

const previousPublicOrigin = process.env.APP_PUBLIC_ORIGIN;

afterEach(() => {
  if (previousPublicOrigin === undefined) {
    delete process.env.APP_PUBLIC_ORIGIN;
  } else {
    process.env.APP_PUBLIC_ORIGIN = previousPublicOrigin;
  }
});

it("publishes a standalone root-scoped manifest with target-owned icons", () => {
  expect(manifest()).toEqual({
    name: "Template",
    short_name: "Template",
    description: "Start from a secure application foundation.",
    start_url: "/",
    scope: "/",
    display: "standalone",
    background_color: "#ffffff",
    theme_color: "#171717",
    icons: [
      { src: "/icon.png", sizes: "512x512", type: "image/png" },
      { src: "/apple-icon.png", sizes: "180x180", type: "image/png" },
      { src: "/favicon.ico", sizes: "any", type: "image/x-icon" },
    ],
  });

  for (const asset of ["icon.png", "apple-icon.png", "favicon.ico"]) {
    const bytes = readFileSync(join(process.cwd(), "src/app", asset));
    expect(bytes.byteLength).toBeGreaterThan(100);
  }
});

it("allows public pages while blocking API, authentication, and protected paths", () => {
  process.env.APP_PUBLIC_ORIGIN = "https://app.example.com";

  expect(robots()).toEqual({
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
    sitemap: "https://app.example.com/sitemap.xml",
    host: "https://app.example.com",
  });
});
