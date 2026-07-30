import {
  isOrganizationSwitchPreservablePath,
  resolveOrganizationSwitchHref,
} from "@/src/features/organizations/organization-switch-navigation";

describe("organization switch navigation", () => {
  it.each([
    ["/w/old", "/w/new"],
    ["/w/old/dashboard", "/w/new/dashboard"],
    ["/w/old/settings", "/w/new/settings"],
    ["/w/old/settings/workspace", "/w/new/settings/workspace"],
    ["/w/old/settings/users", "/w/new/settings/users"],
    ["/w/old/settings/roles", "/w/new/settings/roles"],
  ])("preserves the registered workspace path %s", (current, expected) => {
    expect(isOrganizationSwitchPreservablePath(current)).toBe(true);
    expect(resolveOrganizationSwitchHref(current, "new")).toBe(expected);
  });

  it.each([
    null,
    "",
    "/workspaces",
    "/w/old/custom/deep",
    "/w/old/settings/teams",
    "/w/old/settings/users/detail",
    "/w/old/dashboard/more",
  ])("falls back to the selected dashboard for %s", (current) => {
    expect(isOrganizationSwitchPreservablePath(current)).toBe(false);
    expect(resolveOrganizationSwitchHref(current, "new")).toBe(
      "/w/new/dashboard",
    );
  });

  it("encodes the selected organization key", () => {
    expect(
      resolveOrganizationSwitchHref("/w/old/settings/users", "new team"),
    ).toBe("/w/new%20team/settings/users");
  });
});
