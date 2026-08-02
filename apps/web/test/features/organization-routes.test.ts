import type { Route } from "next";

import { organizationRoutes } from "@/src/features/organizations/organization-routes";

describe("organizationRoutes", () => {
  it("exposes the exact organization route surface", () => {
    expect(organizationRoutes.welcome).toBe("/welcome");
    expect(organizationRoutes.workspaces).toBe("/workspaces");
    expect(organizationRoutes.workspace("acme")).toBe("/w/acme");
    expect(organizationRoutes.dashboard("acme")).toBe("/w/acme/dashboard");
    expect(organizationRoutes.settings("acme")).toBe("/w/acme/settings");
    expect(organizationRoutes.settingsWorkspace("acme")).toBe(
      "/w/acme/settings/workspace",
    );
    expect(organizationRoutes.settingsUsers("acme")).toBe(
      "/w/acme/settings/users",
    );
    expect(organizationRoutes.settingsRoles("acme")).toBe(
      "/w/acme/settings/roles",
    );
    expect(organizationRoutes.settingsApiKeys("acme")).toBe(
      "/w/acme/settings/api-keys",
    );
  });

  it("encodes organization keys as one dynamic route segment", () => {
    expect(organizationRoutes.dashboard("acme team")).toBe(
      "/w/acme%20team/dashboard",
    );
    expect(organizationRoutes.settingsWorkspace("acme/team")).toBe(
      "/w/acme%2Fteam/settings/workspace",
    );
    expect(organizationRoutes.settingsApiKeys("acme/team")).toBe(
      "/w/acme%2Fteam/settings/api-keys",
    );

    const typedRoutes: readonly Route[] = [
      organizationRoutes.welcome,
      organizationRoutes.workspaces,
      organizationRoutes.workspace("acme"),
      organizationRoutes.dashboard("acme"),
      organizationRoutes.settings("acme"),
      organizationRoutes.settingsWorkspace("acme"),
      organizationRoutes.settingsUsers("acme"),
      organizationRoutes.settingsRoles("acme"),
      organizationRoutes.settingsApiKeys("acme"),
    ];
    expect(typedRoutes).toHaveLength(9);
  });
});
