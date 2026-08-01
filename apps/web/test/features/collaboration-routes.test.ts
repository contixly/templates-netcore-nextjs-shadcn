import { accountRoutes } from "@/src/features/account/account-routes";
import { collaborationRoutes } from "@/src/features/collaboration/collaboration-routes";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";

describe("collaboration routes", () => {
  it("encodes organization keys without changing the settings suffix", () => {
    expect(collaborationRoutes.settingsTeams("North / Europe")).toBe(
      "/w/North%20%2F%20Europe/settings/teams",
    );
    expect(collaborationRoutes.settingsInvitations("acme?view=all")).toBe(
      "/w/acme%3Fview%3Dall/settings/invitations",
    );
  });

  it("uses the exact account and opaque invitation decision paths", () => {
    expect(collaborationRoutes.accountInvitations).toBe("/user/invitations");
    expect(
      collaborationRoutes.invitationDecision(
        "01900000-0000-7000-8000-000000000401",
      ),
    ).toBe("/invite/01900000-0000-7000-8000-000000000401");
    expect(collaborationRoutes.invitationDecision("id/with?delimiters")).toBe(
      "/invite/id%2Fwith%3Fdelimiters",
    );
  });

  it("keeps organization and account route contracts aligned", () => {
    expect(organizationRoutes.settingsTeams("North / Europe")).toBe(
      collaborationRoutes.settingsTeams("North / Europe"),
    );
    expect(organizationRoutes.settingsInvitations("North / Europe")).toBe(
      collaborationRoutes.settingsInvitations("North / Europe"),
    );
    expect(accountRoutes.invitations).toBe(
      collaborationRoutes.accountInvitations,
    );
  });
});
