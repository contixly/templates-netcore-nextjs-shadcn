import type { Route } from "next";

import { accountRoutes } from "@/src/features/account/account-routes";
import { apiKeyRoutes } from "@/src/features/api-keys/api-key-routes";
import { applicationRoutes } from "@/src/features/application/application-routes";
import { collaborationRoutes } from "@/src/features/collaboration/collaboration-routes";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";

export type ApplicationPageId =
  | "home"
  | "login"
  | "authError"
  | "dashboard"
  | "welcome"
  | "workspaces"
  | "invitationDecision"
  | "accountProfile"
  | "accountConnections"
  | "accountSecurity"
  | "accountInvitations"
  | "accountApiKeys"
  | "accountDanger"
  | "organization"
  | "organizationDashboard"
  | "organizationWorkspace"
  | "organizationUsers"
  | "organizationRoles"
  | "organizationApiKeys"
  | "organizationTeams"
  | "organizationInvitations";

export type ApplicationPageDefinition = Readonly<{
  id: ApplicationPageId;
  indexable: boolean;
  messageKey: string;
  match: (pathname: string) => boolean;
}>;

const exact = (path: string) => (pathname: string) => pathname === path;
const pattern = (value: RegExp) => (pathname: string) => value.test(pathname);

const catalogRoutePlaceholder = "catalog-workspace";

function dynamicRoutePattern(build: (placeholder: string) => Route): RegExp {
  const route = build(catalogRoutePlaceholder);
  const escaped = route.replace(catalogRoutePlaceholder, "[^/]+");

  return new RegExp(`^${escaped}$`);
}

const organizationRoot = dynamicRoutePattern(organizationRoutes.workspace);
const organizationDashboard = dynamicRoutePattern(organizationRoutes.dashboard);
const organizationWorkspace = dynamicRoutePattern(
  organizationRoutes.settingsWorkspace,
);
const organizationUsers = dynamicRoutePattern(organizationRoutes.settingsUsers);
const organizationRoles = dynamicRoutePattern(organizationRoutes.settingsRoles);
const organizationApiKeys = dynamicRoutePattern(apiKeyRoutes.organization);
const organizationTeams = dynamicRoutePattern(
  collaborationRoutes.settingsTeams,
);
const organizationInvitations = dynamicRoutePattern(
  collaborationRoutes.settingsInvitations,
);
const invitationDecision = dynamicRoutePattern(
  collaborationRoutes.invitationDecision,
);

export const applicationPageCatalog: readonly ApplicationPageDefinition[] = [
  {
    id: "home",
    indexable: true,
    messageKey: "application.pages.home",
    match: exact(applicationRoutes.home),
  },
  {
    id: "login",
    indexable: false,
    messageKey: "application.pages.login",
    match: exact(applicationRoutes.login),
  },
  {
    id: "authError",
    indexable: false,
    messageKey: "application.pages.authError",
    match: exact(applicationRoutes.authError),
  },
  {
    id: "dashboard",
    indexable: false,
    messageKey: "application.pages.dashboard",
    match: exact(applicationRoutes.dashboard),
  },
  {
    id: "welcome",
    indexable: false,
    messageKey: "application.pages.welcome",
    match: exact(applicationRoutes.welcome),
  },
  {
    id: "workspaces",
    indexable: false,
    messageKey: "application.pages.workspaces",
    match: exact(applicationRoutes.workspaces),
  },
  {
    id: "accountProfile",
    indexable: false,
    messageKey: "application.pages.accountProfile",
    match: exact(accountRoutes.profile),
  },
  {
    id: "accountConnections",
    indexable: false,
    messageKey: "application.pages.accountConnections",
    match: exact(accountRoutes.connections),
  },
  {
    id: "accountSecurity",
    indexable: false,
    messageKey: "application.pages.accountSecurity",
    match: exact(accountRoutes.security),
  },
  {
    id: "accountInvitations",
    indexable: false,
    messageKey: "application.pages.accountInvitations",
    match: exact(accountRoutes.invitations),
  },
  {
    id: "accountApiKeys",
    indexable: false,
    messageKey: "application.pages.accountApiKeys",
    match: exact(apiKeyRoutes.personal),
  },
  {
    id: "accountDanger",
    indexable: false,
    messageKey: "application.pages.accountDanger",
    match: exact(accountRoutes.danger),
  },
  {
    id: "organization",
    indexable: false,
    messageKey: "application.pages.organization",
    match: pattern(organizationRoot),
  },
  {
    id: "organizationDashboard",
    indexable: false,
    messageKey: "application.pages.organizationDashboard",
    match: pattern(organizationDashboard),
  },
  {
    id: "organizationWorkspace",
    indexable: false,
    messageKey: "application.pages.organizationWorkspace",
    match: pattern(organizationWorkspace),
  },
  {
    id: "organizationUsers",
    indexable: false,
    messageKey: "application.pages.organizationUsers",
    match: pattern(organizationUsers),
  },
  {
    id: "organizationRoles",
    indexable: false,
    messageKey: "application.pages.organizationRoles",
    match: pattern(organizationRoles),
  },
  {
    id: "organizationApiKeys",
    indexable: false,
    messageKey: "application.pages.organizationApiKeys",
    match: pattern(organizationApiKeys),
  },
  {
    id: "organizationTeams",
    indexable: false,
    messageKey: "application.pages.organizationTeams",
    match: pattern(organizationTeams),
  },
  {
    id: "organizationInvitations",
    indexable: false,
    messageKey: "application.pages.organizationInvitations",
    match: pattern(organizationInvitations),
  },
  {
    id: "invitationDecision",
    indexable: false,
    messageKey: "application.pages.invitationDecision",
    match: pattern(invitationDecision),
  },
];

export function resolveApplicationPage(
  pathname: string,
): ApplicationPageDefinition | null {
  return applicationPageCatalog.find(({ match }) => match(pathname)) ?? null;
}
