import {
  isOrganizationSwitchPreservablePath,
  resolveOrganizationSwitchHref,
} from "@/src/features/organizations/organization-switch-navigation";

describe("organization switch navigation", () => {
  it.each([
    ["/w/old", false, "/w/new"],
    ["/w/old/dashboard", false, "/w/new/dashboard"],
    ["/w/old/settings", false, "/w/new/settings"],
    ["/w/old/settings/workspace", false, "/w/new/settings/workspace"],
    ["/w/old/settings/users", false, "/w/new/settings/users"],
    ["/w/old/settings/roles", false, "/w/new/settings/roles"],
    ["/w/old/settings/teams", false, "/w/new/settings/teams"],
    ["/w/old/settings/invitations", true, "/w/new/settings/invitations"],
  ])(
    "preserves the registered workspace path %s for target invitation capability %s",
    (current, canManageInvitations, expected) => {
      expect(isOrganizationSwitchPreservablePath(current)).toBe(true);
      expect(
        resolveOrganizationSwitchHref(current, "new", canManageInvitations),
      ).toBe(expected);
    },
  );

  it("falls back invitation settings directly to the target dashboard when the target cannot manage invitations", () => {
    expect(
      resolveOrganizationSwitchHref(
        "/w/owner/settings/invitations",
        "member team",
        false,
      ),
    ).toBe("/w/member%20team/dashboard");
  });

  it.each([
    null,
    "",
    "/workspaces",
    "/w/old/custom/deep",
    "/w/old/settings/users/detail",
    "/w/old/dashboard/more",
  ])("falls back to the selected dashboard for %s", (current) => {
    expect(isOrganizationSwitchPreservablePath(current)).toBe(false);
    expect(resolveOrganizationSwitchHref(current, "new", false)).toBe(
      "/w/new/dashboard",
    );
  });

  it("encodes the selected organization key", () => {
    expect(
      resolveOrganizationSwitchHref("/w/old/settings/users", "new team", false),
    ).toBe("/w/new%20team/settings/users");
  });
});
