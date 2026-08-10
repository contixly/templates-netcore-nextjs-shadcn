import { existsSync, readdirSync } from "node:fs";
import { join, relative, resolve } from "node:path";
import type { ReactElement } from "react";

import OrganizationDashboardNavigation from "@/src/app/(protected)/@applicationNavigation/w/[organizationKey]/dashboard/page";
import OrganizationNavigation from "@/src/app/(protected)/@applicationNavigation/w/[organizationKey]/page";
import OrganizationApiKeysNavigation from "@/src/app/(protected)/@applicationNavigation/w/[organizationKey]/settings/api-keys/page";
import OrganizationInvitationsNavigation from "@/src/app/(protected)/@applicationNavigation/w/[organizationKey]/settings/invitations/page";
import OrganizationSettingsNavigation from "@/src/app/(protected)/@applicationNavigation/w/[organizationKey]/settings/page";
import OrganizationRolesNavigation from "@/src/app/(protected)/@applicationNavigation/w/[organizationKey]/settings/roles/page";
import OrganizationTeamsNavigation from "@/src/app/(protected)/@applicationNavigation/w/[organizationKey]/settings/teams/page";
import OrganizationUsersNavigation from "@/src/app/(protected)/@applicationNavigation/w/[organizationKey]/settings/users/page";
import OrganizationWorkspaceNavigation from "@/src/app/(protected)/@applicationNavigation/w/[organizationKey]/settings/workspace/page";
import WorkspacesNavigation from "@/src/app/(protected)/@applicationNavigation/workspaces/page";
import type { ApplicationNavigationSlotProps } from "@/src/features/application/ui/application-navigation-slot";

jest.mock("next/server", () => ({
  connection: jest.fn().mockResolvedValue(undefined),
}));

function protectedPageFiles(root: string): string[] {
  return readdirSync(root, { withFileTypes: true }).flatMap((entry) => {
    const path = join(root, entry.name);

    if (entry.name === "@applicationNavigation") {
      return [];
    }

    if (entry.isDirectory()) {
      return protectedPageFiles(path);
    }

    return entry.name === "page.tsx" ? [path] : [];
  });
}

function slotProps(
  element: ReactElement<ApplicationNavigationSlotProps>,
): ApplicationNavigationSlotProps {
  return element.props;
}

const params = (organizationKey: string) =>
  Promise.resolve({ organizationKey });

it("passes every organization route's exact URL and key to the shared slot", async () => {
  const cases = [
    [OrganizationNavigation, "/w/acme"],
    [OrganizationDashboardNavigation, "/w/acme/dashboard"],
    [OrganizationSettingsNavigation, "/w/acme/settings"],
    [OrganizationWorkspaceNavigation, "/w/acme/settings/workspace"],
    [OrganizationUsersNavigation, "/w/acme/settings/users"],
    [OrganizationRolesNavigation, "/w/acme/settings/roles"],
    [OrganizationApiKeysNavigation, "/w/acme/settings/api-keys"],
    [OrganizationTeamsNavigation, "/w/acme/settings/teams"],
    [OrganizationInvitationsNavigation, "/w/acme/settings/invitations"],
  ] as const;

  for (const [Navigation, redirectPath] of cases) {
    const element = await Navigation({ params: params("acme") });
    expect(slotProps(element)).toEqual({
      redirectPath,
      organizationKey: "acme",
    });
  }
});

it("keeps non-organization navigation free of an organization key", () => {
  expect(slotProps(WorkspacesNavigation())).toEqual({
    redirectPath: "/workspaces",
  });
});

it("has a matching navigation leaf for every protected page leaf", () => {
  const protectedRoot = resolve(process.cwd(), "src/app/(protected)");
  const slotRoot = join(protectedRoot, "@applicationNavigation");
  const pageLeaves = protectedPageFiles(protectedRoot);

  expect(pageLeaves).not.toHaveLength(0);
  for (const page of pageLeaves) {
    expect(existsSync(join(slotRoot, relative(protectedRoot, page)))).toBe(
      true,
    );
  }
});
