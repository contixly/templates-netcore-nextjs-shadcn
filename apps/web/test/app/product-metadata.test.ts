/** @jest-environment node */

import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";

import {
  applicationPageCatalog,
  type ApplicationPageId,
} from "@/src/features/application/application-page-catalog";
import {
  buildApplicationPageMetadata,
  resolveOpenGraphLocale,
} from "@/src/lib/metadata";
import OpenGraphImage, { contentType, size } from "@/src/app/opengraph-image";
import TwitterImage from "@/src/app/twitter-image";

jest.mock("next/og", () => ({
  ImageResponse: class extends Response {
    readonly element: unknown;

    constructor(element: unknown) {
      super(null, { headers: { "content-type": "image/png" } });
      this.element = element;
    }
  },
}));

const previousPublicOrigin = process.env.APP_PUBLIC_ORIGIN;

afterEach(() => {
  if (previousPublicOrigin === undefined) {
    delete process.env.APP_PUBLIC_ORIGIN;
  } else {
    process.env.APP_PUBLIC_ORIGIN = previousPublicOrigin;
  }
});

describe("application page metadata", () => {
  beforeEach(() => {
    process.env.APP_PUBLIC_ORIGIN = "https://app.example.com";
  });

  it("builds localized protected metadata without protected runtime values", async () => {
    const metadata = await buildApplicationPageMetadata("dashboard", "ru");

    expect(metadata).toMatchObject({
      title: "Панель управления",
      description:
        "Просматривайте активность и быстрые действия рабочего пространства.",
      robots: { index: false, follow: false },
      openGraph: {
        title: "Панель управления",
        locale: "ru_RU",
        siteName: "Template",
        url: null,
      },
      alternates: { canonical: null },
    });
    expect(JSON.stringify(metadata)).not.toMatch(
      /organization-name|user@example\.com|private-workspace/iu,
    );
  });

  it("makes only the localized product home indexable and canonical", async () => {
    const metadataEntries = await Promise.all(
      applicationPageCatalog.map(
        async ({ id }) =>
          [id, await buildApplicationPageMetadata(id, "en")] as const,
      ),
    );

    const home = metadataEntries.find(([id]) => id === "home")?.[1];
    expect(home).toMatchObject({
      metadataBase: new URL("https://app.example.com"),
      title: expect.stringContaining("Template"),
      alternates: { canonical: "https://app.example.com/" },
      robots: { index: true, follow: true },
      openGraph: {
        locale: "en_US",
        url: "https://app.example.com/",
        images: [
          {
            url: "https://app.example.com/opengraph-image",
            width: 1200,
            height: 630,
          },
        ],
      },
      twitter: {
        card: "summary_large_image",
        images: ["https://app.example.com/twitter-image"],
      },
    });

    for (const [id, metadata] of metadataEntries) {
      if (id !== "home") {
        expect(metadata.robots).toEqual({ index: false, follow: false });
        expect(metadata.alternates).toEqual({ canonical: null });
        expect(metadata.openGraph).toEqual(
          expect.objectContaining({ url: null }),
        );
      }
    }
  });

  it("maps only the closed application locales to Open Graph locales", () => {
    expect(resolveOpenGraphLocale("en")).toBe("en_US");
    expect(resolveOpenGraphLocale("ru")).toBe("ru_RU");
  });
});

describe("product metadata page inventory", () => {
  const metadataPages = {
    "(protected)/dashboard/page.tsx": "dashboard",
    "(protected)/invite/[invitationId]/page.tsx": "invitationDecision",
    "(protected)/user/api-keys/page.tsx": "accountApiKeys",
    "(protected)/user/connections/page.tsx": "accountConnections",
    "(protected)/user/danger/page.tsx": "accountDanger",
    "(protected)/user/invitations/page.tsx": "accountInvitations",
    "(protected)/user/profile/page.tsx": "accountProfile",
    "(protected)/user/security/page.tsx": "accountSecurity",
    "(protected)/w/[organizationKey]/dashboard/page.tsx":
      "organizationDashboard",
    "(protected)/w/[organizationKey]/page.tsx": "organization",
    "(protected)/w/[organizationKey]/settings/page.tsx":
      "organizationWorkspace",
    "(protected)/w/[organizationKey]/settings/api-keys/page.tsx":
      "organizationApiKeys",
    "(protected)/w/[organizationKey]/settings/invitations/page.tsx":
      "organizationInvitations",
    "(protected)/w/[organizationKey]/settings/roles/page.tsx":
      "organizationRoles",
    "(protected)/w/[organizationKey]/settings/teams/page.tsx":
      "organizationTeams",
    "(protected)/w/[organizationKey]/settings/users/page.tsx":
      "organizationUsers",
    "(protected)/w/[organizationKey]/settings/workspace/page.tsx":
      "organizationWorkspace",
    "(protected)/welcome/page.tsx": "welcome",
    "(protected)/workspaces/page.tsx": "workspaces",
    "(simple)/auth/error/page.tsx": "authError",
    "(simple)/auth/login/page.tsx": "login",
  } satisfies Record<string, ApplicationPageId>;

  const redirectOnlyPages = ["(protected)/user/page.tsx"];

  it("classifies each product page as catalog-backed metadata or redirect-only", () => {
    const appDirectory = join(process.cwd(), "src/app");
    const inventory = [
      ...Object.keys(metadataPages),
      ...redirectOnlyPages,
    ].sort();
    const actual = ["(protected)", "(simple)"].flatMap((routeGroup) => {
      const root = join(appDirectory, routeGroup);
      const visit = (directory: string): string[] => {
        return readdirSync(directory, { withFileTypes: true }).flatMap(
          (entry) => {
            if (entry.name.startsWith("@")) return [];
            const path = join(directory, entry.name);
            if (entry.isDirectory()) return visit(path);
            return entry.name === "page.tsx"
              ? [path.slice(appDirectory.length + 1)]
              : [];
          },
        );
      };
      return visit(root);
    });

    expect(actual.sort()).toEqual(inventory);

    for (const [page, pageId] of Object.entries(metadataPages)) {
      const source = readFileSync(join(appDirectory, page), "utf8");
      expect(source).toContain(`buildApplicationPageMetadata("${pageId}")`);
    }

    for (const page of redirectOnlyPages) {
      const source = readFileSync(join(appDirectory, page), "utf8");
      expect(source).not.toContain("buildApplicationPageMetadata(");
      expect(source).toMatch(
        /export default function \w+\(\): never \{\s+redirect\([^)]+\);\s+\}/u,
      );
    }
  });
});

describe("root social images", () => {
  it("generates deterministic target-owned 1200 by 630 images for both surfaces", async () => {
    expect(size).toEqual({ width: 1200, height: 630 });
    expect(contentType).toBe("image/png");

    const [openGraph, twitter] = await Promise.all([
      OpenGraphImage(),
      TwitterImage(),
    ]);

    expect(openGraph.headers.get("content-type")).toBe("image/png");
    expect(twitter.headers.get("content-type")).toBe("image/png");
    expect(
      JSON.stringify((openGraph as Response & { element: unknown }).element),
    ).toContain("Template");
    expect(
      JSON.stringify((openGraph as Response & { element: unknown }).element),
    ).not.toMatch(/session|organization|user@example/iu);
  });
});
