/** @jest-environment node */

import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

import {
  resolveApplicationPage,
  type ApplicationPageId,
} from "@/src/features/application/application-page-catalog";

type MetadataDecision =
  | "public-index"
  | "authentication-noindex"
  | "protected-noindex"
  | "protected-unconditional-redirect-noindex"
  | "protected-conditional-redirect-noindex"
  | "documents-index"
  | "documents-article";

type RouteParityEntry = Readonly<{
  url: string;
  pathname: string;
  page: string;
  navigationSlot: string | null;
  pageId: ApplicationPageId | null;
  metadata: MetadataDecision;
}>;

const protectedRoot = "src/app/(protected)";
const navigationRoot = `${protectedRoot}/@applicationNavigation`;

const routeParityInventory = [
  {
    url: "/",
    pathname: "/",
    page: "src/app/(public)/(home)/page.tsx",
    navigationSlot: null,
    pageId: "home",
    metadata: "public-index",
  },
  {
    url: "/auth/login",
    pathname: "/auth/login",
    page: "src/app/(simple)/auth/login/page.tsx",
    navigationSlot: null,
    pageId: "login",
    metadata: "authentication-noindex",
  },
  {
    url: "/auth/error",
    pathname: "/auth/error",
    page: "src/app/(simple)/auth/error/page.tsx",
    navigationSlot: null,
    pageId: "authError",
    metadata: "authentication-noindex",
  },
  {
    url: "/dashboard",
    pathname: "/dashboard",
    page: `${protectedRoot}/dashboard/page.tsx`,
    navigationSlot: `${navigationRoot}/dashboard/page.tsx`,
    pageId: "dashboard",
    metadata: "protected-noindex",
  },
  {
    url: "/welcome",
    pathname: "/welcome",
    page: `${protectedRoot}/welcome/page.tsx`,
    navigationSlot: `${navigationRoot}/welcome/page.tsx`,
    pageId: "welcome",
    metadata: "protected-noindex",
  },
  {
    url: "/workspaces",
    pathname: "/workspaces",
    page: `${protectedRoot}/workspaces/page.tsx`,
    navigationSlot: `${navigationRoot}/workspaces/page.tsx`,
    pageId: "workspaces",
    metadata: "protected-noindex",
  },
  {
    url: "/invite/{invitationId}",
    pathname: "/invite/inventory-invitation",
    page: `${protectedRoot}/invite/[invitationId]/page.tsx`,
    navigationSlot: `${navigationRoot}/invite/[invitationId]/page.tsx`,
    pageId: "invitationDecision",
    metadata: "protected-noindex",
  },
  {
    url: "/user",
    pathname: "/user",
    page: `${protectedRoot}/user/page.tsx`,
    navigationSlot: `${navigationRoot}/user/page.tsx`,
    pageId: null,
    metadata: "protected-unconditional-redirect-noindex",
  },
  ...(
    [
      ["profile", "accountProfile"],
      ["invitations", "accountInvitations"],
      ["connections", "accountConnections"],
      ["security", "accountSecurity"],
      ["api-keys", "accountApiKeys"],
      ["danger", "accountDanger"],
    ] as const
  ).map(([segment, pageId]) => ({
    url: `/user/${segment}`,
    pathname: `/user/${segment}`,
    page: `${protectedRoot}/user/${segment}/page.tsx`,
    navigationSlot: `${navigationRoot}/user/${segment}/page.tsx`,
    pageId,
    metadata: "protected-noindex" as const,
  })),
  {
    url: "/w/{organizationKey}",
    pathname: "/w/inventory-workspace",
    page: `${protectedRoot}/w/[organizationKey]/page.tsx`,
    navigationSlot: `${navigationRoot}/w/[organizationKey]/page.tsx`,
    pageId: "organization",
    metadata: "protected-conditional-redirect-noindex",
  },
  {
    url: "/w/{organizationKey}/dashboard",
    pathname: "/w/inventory-workspace/dashboard",
    page: `${protectedRoot}/w/[organizationKey]/dashboard/page.tsx`,
    navigationSlot: `${navigationRoot}/w/[organizationKey]/dashboard/page.tsx`,
    pageId: "organizationDashboard",
    metadata: "protected-noindex",
  },
  {
    url: "/w/{organizationKey}/settings",
    pathname: "/w/inventory-workspace/settings",
    page: `${protectedRoot}/w/[organizationKey]/settings/page.tsx`,
    navigationSlot: `${navigationRoot}/w/[organizationKey]/settings/page.tsx`,
    pageId: "organizationWorkspace",
    metadata: "protected-conditional-redirect-noindex",
  },
  ...(
    [
      ["workspace", "organizationWorkspace"],
      ["invitations", "organizationInvitations"],
      ["users", "organizationUsers"],
      ["teams", "organizationTeams"],
      ["roles", "organizationRoles"],
      ["api-keys", "organizationApiKeys"],
    ] as const
  ).map(([segment, pageId]) => ({
    url: `/w/{organizationKey}/settings/${segment}`,
    pathname: `/w/inventory-workspace/settings/${segment}`,
    page: `${protectedRoot}/w/[organizationKey]/settings/${segment}/page.tsx`,
    navigationSlot: `${navigationRoot}/w/[organizationKey]/settings/${segment}/page.tsx`,
    pageId,
    metadata: "protected-noindex" as const,
  })),
  {
    url: "/docs",
    pathname: "/docs",
    page: "src/app/(documents)/docs/page.tsx",
    navigationSlot: null,
    pageId: null,
    metadata: "documents-index",
  },
  {
    url: "/docs/**",
    pathname: "/docs/application",
    page: "src/app/(documents)/docs/[...slug]/page.tsx",
    navigationSlot: null,
    pageId: null,
    metadata: "documents-article",
  },
] as const satisfies readonly RouteParityEntry[];

const expectedDesignUrls = [
  "/",
  "/auth/login",
  "/auth/error",
  "/dashboard",
  "/welcome",
  "/workspaces",
  "/invite/{invitationId}",
  "/user",
  "/user/profile",
  "/user/invitations",
  "/user/connections",
  "/user/security",
  "/user/api-keys",
  "/user/danger",
  "/w/{organizationKey}",
  "/w/{organizationKey}/dashboard",
  "/w/{organizationKey}/settings",
  "/w/{organizationKey}/settings/workspace",
  "/w/{organizationKey}/settings/invitations",
  "/w/{organizationKey}/settings/users",
  "/w/{organizationKey}/settings/teams",
  "/w/{organizationKey}/settings/roles",
  "/w/{organizationKey}/settings/api-keys",
  "/docs",
  "/docs/**",
] as const;

const expectedMetadataDecisions = [
  ["/", "public-index"],
  ["/auth/login", "authentication-noindex"],
  ["/auth/error", "authentication-noindex"],
  ["/dashboard", "protected-noindex"],
  ["/welcome", "protected-noindex"],
  ["/workspaces", "protected-noindex"],
  ["/invite/{invitationId}", "protected-noindex"],
  ["/user", "protected-unconditional-redirect-noindex"],
  ["/user/profile", "protected-noindex"],
  ["/user/invitations", "protected-noindex"],
  ["/user/connections", "protected-noindex"],
  ["/user/security", "protected-noindex"],
  ["/user/api-keys", "protected-noindex"],
  ["/user/danger", "protected-noindex"],
  ["/w/{organizationKey}", "protected-conditional-redirect-noindex"],
  ["/w/{organizationKey}/dashboard", "protected-noindex"],
  ["/w/{organizationKey}/settings", "protected-conditional-redirect-noindex"],
  ["/w/{organizationKey}/settings/workspace", "protected-noindex"],
  ["/w/{organizationKey}/settings/invitations", "protected-noindex"],
  ["/w/{organizationKey}/settings/users", "protected-noindex"],
  ["/w/{organizationKey}/settings/teams", "protected-noindex"],
  ["/w/{organizationKey}/settings/roles", "protected-noindex"],
  ["/w/{organizationKey}/settings/api-keys", "protected-noindex"],
  ["/docs", "documents-index"],
  ["/docs/**", "documents-article"],
] as const satisfies readonly (readonly [string, MetadataDecision])[];

const noindexLayoutSources = {
  "authentication-noindex": "src/app/(simple)/layout.tsx",
  "protected-conditional-redirect-noindex": `${protectedRoot}/layout.tsx`,
  "protected-noindex": `${protectedRoot}/layout.tsx`,
  "protected-unconditional-redirect-noindex": `${protectedRoot}/layout.tsx`,
} as const;

function readSource(path: string) {
  return readFileSync(resolve(process.cwd(), path), "utf8");
}

function expectNoindexLayout(path: string) {
  const source = readSource(path);
  expect(source).toMatch(
    /robots:\s*\{\s*index:\s*false,\s*follow:\s*false\s*\}/,
  );
  expect(source).toMatch(/alternates:\s*\{\s*canonical:\s*null\s*\}/);
  expect(source).toMatch(/openGraph:\s*\{\s*url:\s*null\s*\}/);
}

function hasNoindexLayout(
  metadata: MetadataDecision,
): metadata is keyof typeof noindexLayoutSources {
  return Object.hasOwn(noindexLayoutSources, metadata);
}

describe("iteration 9 route parity inventory", () => {
  it("accounts for every approved design URL exactly once", () => {
    expect(routeParityInventory.map(({ url }) => url)).toEqual(
      expectedDesignUrls,
    );
  });

  it("pins the metadata and redirect decision for every design URL", () => {
    expect(
      routeParityInventory.map(({ metadata, url }) => [url, metadata]),
    ).toEqual(expectedMetadataDecisions);
  });

  it.each(routeParityInventory)(
    "$url has a target page and the declared navigation-slot leaf",
    ({ navigationSlot, page }) => {
      expect(existsSync(resolve(process.cwd(), page))).toBe(true);
      if (navigationSlot) {
        expect(existsSync(resolve(process.cwd(), navigationSlot))).toBe(true);
      }
    },
  );

  it.each(routeParityInventory.filter(({ pageId }) => pageId !== null))(
    "$url resolves its localized page ID",
    ({ pageId, pathname }) => {
      const page = resolveApplicationPage(pathname);
      expect(page?.id).toBe(pageId);
      expect(page?.messageKey).toBe(`application.pages.${pageId}`);

      for (const locale of ["en", "ru"] as const) {
        const messages = JSON.parse(
          readFileSync(
            resolve(process.cwd(), `src/messages/application.${locale}.json`),
            "utf8",
          ),
        ) as { pages: Record<string, { description: string; title: string }> };
        expect(messages.pages[pageId!]).toEqual({
          description: expect.any(String),
          title: expect.any(String),
        });
      }
    },
  );

  it.each(routeParityInventory)(
    "$url enforces its exact metadata and redirect decision",
    ({ metadata, page, pageId, pathname }) => {
      const pageSource = readSource(page);

      if (metadata === "public-index") {
        expect(resolveApplicationPage(pathname)?.indexable).toBe(true);
        expect(pageId).toBe("home");
        expect(readSource("src/app/(public)/(home)/layout.tsx")).toContain(
          `buildApplicationPageMetadata("${pageId}", locale)`,
        );
        return;
      }

      if (hasNoindexLayout(metadata)) {
        expectNoindexLayout(noindexLayoutSources[metadata]);
      }

      if (
        metadata === "authentication-noindex" ||
        metadata === "protected-noindex" ||
        metadata === "protected-conditional-redirect-noindex"
      ) {
        expect(resolveApplicationPage(pathname)?.indexable).toBe(false);
        expect(pageId).not.toBeNull();
        expect(pageSource).toContain(
          `buildApplicationPageMetadata("${pageId}")`,
        );
      }

      if (metadata === "protected-noindex") {
        expect(pageSource).not.toMatch(
          /export default function \w+\(\): never\s*\{\s*redirect\(/,
        );
        return;
      }

      if (metadata === "protected-unconditional-redirect-noindex") {
        expect(pageId).toBeNull();
        expect(pageSource).not.toContain("buildApplicationPageMetadata");
        expect(pageSource).toMatch(
          /export default function \w+\(\): never\s*\{\s*redirect\(/,
        );
        return;
      }

      if (metadata === "protected-conditional-redirect-noindex") {
        expect(pageSource).toMatch(/\bredirect\(/);
        expect(pageSource).toMatch(/\breturn\s+</);
        expect(pageSource).toMatch(/\bforbidden\(\)/);
        return;
      }

      if (metadata === "documents-index") {
        expect(pageId).toBeNull();
        expect(page).toBe("src/app/(documents)/docs/page.tsx");
        expect(pageSource).toContain("export async function generateMetadata");
        expect(pageSource).toContain("documentsRoutes.root");
        expect(pageSource).toContain('type: "website"');
        expect(pageSource).not.toContain("permanentRedirect");
        return;
      }

      if (metadata === "documents-article") {
        expect(pageId).toBeNull();
        expect(page).toBe("src/app/(documents)/docs/[...slug]/page.tsx");
        expect(pageSource).toContain("export async function generateMetadata");
        expect(pageSource).toContain("canonicalDocumentUrlFromSlug");
        expect(pageSource).toContain('type: "article"');
        expect(pageSource).toContain("permanentRedirect(documentsRoutes.root)");
      }
    },
  );
});
