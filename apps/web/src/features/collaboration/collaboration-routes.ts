import type { Route } from "next";

function organizationSettingsPath(
  organizationKey: string,
  section: "teams" | "invitations",
): Route {
  return `/w/${encodeURIComponent(organizationKey)}/settings/${section}` as Route;
}

export const collaborationRoutes = {
  settingsTeams: (organizationKey: string) =>
    organizationSettingsPath(organizationKey, "teams"),
  settingsInvitations: (organizationKey: string) =>
    organizationSettingsPath(organizationKey, "invitations"),
  accountInvitations: "/user/invitations" as Route,
  invitationDecision: (invitationId: string) =>
    `/invite/${encodeURIComponent(invitationId)}` as Route,
} as const;
