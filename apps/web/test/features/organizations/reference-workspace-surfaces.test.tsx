import { screen } from "@testing-library/react";

import { OrganizationSettingsNav } from "@/src/features/organizations/ui/organization-settings-nav";
import { renderWithMessages } from "@/test/support/render";

test("workspace settings navigation uses the reference 16-rem desktop sidebar rail", () => {
  renderWithMessages(
    <OrganizationSettingsNav
      canManageApiKeys
      canManageInvitations
      organizationKey="acme"
      pathname="/w/acme/settings/workspace"
    />,
  );

  const navigation = screen.getByRole("navigation", {
    name: "Workspace settings",
  });
  expect(navigation).toHaveClass("md:w-64");
  expect(screen.getByRole("link", { name: "Workspace" })).toHaveAttribute(
    "aria-current",
    "page",
  );
});
