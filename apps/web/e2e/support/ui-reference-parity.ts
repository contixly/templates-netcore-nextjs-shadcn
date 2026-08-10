import type { BrowserContext, Page } from "@playwright/test";

import { accountRoutes } from "@/src/features/account/account-routes";
import { apiKeyRoutes } from "@/src/features/api-keys/api-key-routes";
import { applicationRoutes } from "@/src/features/application/application-routes";
import { authenticationRoutes } from "@/src/features/authentication/authentication-routes";
import { collaborationRoutes } from "@/src/features/collaboration/collaboration-routes";
import { dashboardRoutes } from "@/src/features/dashboard/dashboard-routes";
import { documentsRoutes } from "@/src/features/documents/documents-routes";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import type {
  InvitationResponse,
  OrganizationDetailResponse,
  TeamResponse,
} from "@/src/lib/api/generated";

import {
  addGeneratedOrganizationMember,
  createGeneratedInvitation,
  createGeneratedTeam,
} from "./generated-collaboration-api";
import { createGeneratedOrganizationKey } from "./generated-api-keys-api";
import { signInLocalAutomationUser } from "./generated-auth-api";
import type {
  OrganizationTestIdentity,
  TrackedLocalAutomationScenario,
} from "./organization-test-fixture";

export type ReferenceParityRouteId =
  | "home"
  | "login"
  | "auth-error"
  | "docs"
  | "docs-article"
  | "welcome"
  | "workspaces"
  | "dashboard"
  | "organization-dashboard"
  | "user-profile"
  | "user-connections"
  | "user-security"
  | "user-danger"
  | "user-api-keys"
  | "user-invitations"
  | "workspace-settings"
  | "workspace-members"
  | "workspace-roles"
  | "workspace-teams"
  | "workspace-invitations"
  | "workspace-api-keys"
  | "invitation-decision";

export type ReferenceParityPathFixture = Readonly<{
  invitationId: string;
  organizationKey: string;
}>;

type ReferenceParityPath =
  string | ((fixture: ReferenceParityPathFixture) => string);

export type ReferenceParityRoute = Readonly<{
  authentication: "anonymous" | "authenticated";
  captureReadySelector?: string;
  expectedPath?: ReferenceParityPath;
  id: ReferenceParityRouteId;
  path: ReferenceParityPath;
  readySelector: string;
  requiresOrganization?: true;
  scrollStates?: readonly ReferenceParityScrollState[];
  volatileSelectors?: readonly string[];
}>;

export type ReferenceParityScrollState = Readonly<{
  anchorSelector: string;
  id: string;
}>;

export const referenceParityRussianOverflowRouteIds = [
  "docs",
  "user-profile",
  "workspace-settings",
  "workspace-invitations",
  "workspace-api-keys",
] as const satisfies readonly ReferenceParityRouteId[];

const profileVolatileSelectors = [
  "main time",
  "main [data-slot='settings-section'] .font-mono",
] as const;

export const referenceParityRoutes = [
  {
    id: "home",
    path: applicationRoutes.home,
    authentication: "anonymous",
    readySelector: "main#main-content h1",
  },
  {
    id: "login",
    path: authenticationRoutes.login,
    authentication: "anonymous",
    readySelector: "main [data-interaction-ready='true']",
  },
  {
    id: "auth-error",
    path: `${authenticationRoutes.error}?code=external_auth_failed`,
    authentication: "anonymous",
    readySelector: "main h1",
  },
  {
    id: "docs",
    path: documentsRoutes.root,
    authentication: "anonymous",
    readySelector: "main article[aria-labelledby='document-title']",
  },
  {
    id: "docs-article",
    path: documentsRoutes.document("api/api-v1"),
    authentication: "anonymous",
    readySelector: "main article[aria-labelledby='document-title']",
    scrollStates: [
      { id: "problem-details", anchorSelector: "#problem-details" },
    ],
  },
  {
    id: "welcome",
    path: applicationRoutes.welcome,
    authentication: "authenticated",
    readySelector: "main#main-content [data-slot='card']",
    volatileSelectors: ["main article p:last-child"],
  },
  {
    id: "workspaces",
    path: applicationRoutes.workspaces,
    authentication: "authenticated",
    readySelector: "main#main-content [role='article']",
    requiresOrganization: true,
  },
  {
    id: "dashboard",
    path: dashboardRoutes.application,
    expectedPath: ({ organizationKey }) =>
      dashboardRoutes.organization(organizationKey),
    authentication: "authenticated",
    readySelector: "main#main-content [data-slot='table-container']",
    requiresOrganization: true,
  },
  {
    id: "organization-dashboard",
    path: ({ organizationKey }) =>
      dashboardRoutes.organization(organizationKey),
    authentication: "authenticated",
    readySelector: "main#main-content [data-slot='table-container']",
    requiresOrganization: true,
    scrollStates: [
      {
        id: "activity-table",
        anchorSelector: "main#main-content [data-slot='table-container']",
      },
    ],
  },
  {
    id: "user-profile",
    path: accountRoutes.profile,
    authentication: "authenticated",
    readySelector: "main#main-content [data-slot='settings-page-section']",
    requiresOrganization: true,
    scrollStates: [
      { id: "profile-form", anchorSelector: "#account-display-name" },
    ],
    volatileSelectors: profileVolatileSelectors,
  },
  {
    id: "user-connections",
    path: accountRoutes.connections,
    authentication: "authenticated",
    readySelector: "main#main-content [data-slot='settings-page-section']",
    requiresOrganization: true,
  },
  {
    id: "user-security",
    path: accountRoutes.security,
    authentication: "authenticated",
    readySelector: "main#main-content [data-slot='settings-page-section']",
    requiresOrganization: true,
    volatileSelectors: ["main time"],
  },
  {
    id: "user-danger",
    path: accountRoutes.danger,
    authentication: "authenticated",
    readySelector: "main#main-content [data-slot='settings-page-section']",
    requiresOrganization: true,
  },
  {
    id: "user-api-keys",
    path: apiKeyRoutes.personal,
    authentication: "authenticated",
    readySelector: "main#main-content [data-slot='settings-page-section']",
    requiresOrganization: true,
  },
  {
    id: "user-invitations",
    path: collaborationRoutes.accountInvitations,
    authentication: "authenticated",
    readySelector: "main#main-content [data-slot='settings-page-section']",
    requiresOrganization: true,
    volatileSelectors: ["main article p:last-child"],
  },
  {
    id: "workspace-settings",
    path: ({ organizationKey }) =>
      organizationRoutes.settingsWorkspace(organizationKey),
    authentication: "authenticated",
    readySelector: "main#main-content [data-slot='settings-page-section']",
    requiresOrganization: true,
    scrollStates: [
      {
        id: "workspace-form",
        anchorSelector: "#organization-settings-domains",
      },
    ],
  },
  {
    id: "workspace-members",
    path: ({ organizationKey }) =>
      organizationRoutes.settingsUsers(organizationKey),
    authentication: "authenticated",
    captureReadySelector:
      "main#main-content [data-slot='settings-section'] [data-organization-control-interaction-ready='true']",
    readySelector: "main#main-content [data-slot='settings-page-section']",
    requiresOrganization: true,
    scrollStates: [
      {
        id: "member-table",
        anchorSelector:
          "main#main-content [data-slot='settings-section'] table",
      },
    ],
    volatileSelectors: [
      "main [data-slot='settings-section'] dd.font-medium",
      "main [data-slot='table-body'] [data-slot='table-cell']:nth-child(4)",
    ],
  },
  {
    id: "workspace-roles",
    path: ({ organizationKey }) =>
      organizationRoutes.settingsRoles(organizationKey),
    authentication: "authenticated",
    readySelector: "main#main-content [data-slot='settings-page-section']",
    requiresOrganization: true,
  },
  {
    id: "workspace-teams",
    path: ({ organizationKey }) =>
      collaborationRoutes.settingsTeams(organizationKey),
    authentication: "authenticated",
    readySelector: "main#main-content [data-slot='settings-page-section']",
    requiresOrganization: true,
  },
  {
    id: "workspace-invitations",
    path: ({ organizationKey }) =>
      collaborationRoutes.settingsInvitations(organizationKey),
    authentication: "authenticated",
    readySelector: "main#main-content [data-slot='settings-page-section']",
    requiresOrganization: true,
    scrollStates: [
      {
        id: "invitation-table",
        anchorSelector:
          "main#main-content [data-slot='settings-section'] table",
      },
    ],
    volatileSelectors: [
      "main [data-slot='table-body'] [data-slot='table-cell']:nth-child(5)",
      "main [data-slot='table-body'] [data-slot='table-cell']:nth-child(6)",
    ],
  },
  {
    id: "workspace-api-keys",
    path: ({ organizationKey }) =>
      organizationRoutes.settingsApiKeys(organizationKey),
    authentication: "authenticated",
    captureReadySelector:
      "main#main-content [data-slot='settings-section'] [data-interaction-ready='true']",
    readySelector: "main#main-content [data-slot='settings-page-section']",
    requiresOrganization: true,
    scrollStates: [
      {
        id: "api-key-table",
        anchorSelector:
          "main#main-content [data-slot='settings-section'] table",
      },
    ],
    volatileSelectors: [
      "main [data-slot='table-body'] [data-slot='table-cell']:nth-child(2) code",
      "main [data-slot='table-body'] [data-slot='table-cell']:nth-child(5) time",
      "main [data-slot='table-body'] [data-slot='table-cell']:nth-child(6) time",
      "main [data-slot='table-body'] [data-slot='table-cell']:nth-child(7) time",
    ],
  },
  {
    id: "invitation-decision",
    path: ({ invitationId }) =>
      collaborationRoutes.invitationDecision(invitationId),
    authentication: "authenticated",
    readySelector: "main#main-content [data-slot='settings-content-rail']",
    requiresOrganization: true,
    volatileSelectors: [
      "main dl > div:nth-child(6) dd",
      "main dl > div:nth-child(7) dd",
    ],
  },
] as const satisfies readonly ReferenceParityRoute[];

export function buildReferenceParityPath(
  route: ReferenceParityRoute,
  fixture: ReferenceParityPathFixture,
): string {
  return typeof route.path === "function" ? route.path(fixture) : route.path;
}

export function buildReferenceParityExpectedPath(
  route: ReferenceParityRoute,
  fixture: ReferenceParityPathFixture,
): string {
  const expected = route.expectedPath ?? route.path;
  return typeof expected === "function" ? expected(fixture) : expected;
}

type ReferenceParityOrganizationScenario = Readonly<{
  createContext(label: string): Promise<BrowserContext>;
  createLocalUsers(
    preparedUsers: readonly ReturnType<
      ReferenceParityOrganizationScenario["prepareLocalUser"]
    >[],
  ): Promise<TrackedLocalAutomationScenario[]>;
  createOrganization(
    scenario: TrackedLocalAutomationScenario,
    request: BrowserContext["request"],
    name: string,
  ): Promise<OrganizationDetailResponse>;
  prepareLocalUser(
    context: BrowserContext,
    identity: OrganizationTestIdentity,
    label: string,
  ): Readonly<{
    context: BrowserContext;
    identity: OrganizationTestIdentity;
    teardown: TrackedLocalAutomationScenario["teardown"];
  }>;
}>;

export type ReferenceParityOrganizationFixture = Readonly<{
  invitation: InvitationResponse;
  organization: OrganizationDetailResponse;
  outgoingInvitation: InvitationResponse;
  team: TeamResponse;
}>;

export type ReferenceParityFixture = Readonly<{
  assertSafeScreenshot(page: Page): Promise<void>;
  invitationId: string;
  ownerIdentity: OrganizationTestIdentity;
  createOrganizationFixture(): Promise<ReferenceParityOrganizationFixture>;
  ensureAlternateOrganization(): Promise<void>;
  pathFixture(): ReferenceParityPathFixture;
  signIn(page: Page): Promise<void>;
}>;

function safeProjectSlug(projectName: string): string {
  const slug = projectName.toLowerCase().replaceAll(/[^a-z0-9]+/gu, "-");
  if (!slug || slug.length > 32) {
    throw new Error(`Unsupported visual project name: ${projectName}.`);
  }
  return slug;
}

function identity(
  projectName: string,
  role: "member" | "owner" | "invite-sender",
  password: string,
): OrganizationTestIdentity {
  const slug = safeProjectSlug(projectName);
  const roleLabel =
    role === "owner" ? "Owner" : role === "member" ? "Member" : "Invite Sender";
  return {
    email: `local-agent+visual-${slug}-${role}@local-agent.test`,
    name: `Visual ${projectName} ${roleLabel}`,
    password,
  };
}

function directoryMemberIdentity(
  projectName: string,
  index: number,
  password: string,
): OrganizationTestIdentity {
  const slug = safeProjectSlug(projectName);
  const suffix = String(index + 1).padStart(2, "0");
  return {
    email: `local-agent+visual-${slug}-directory-${suffix}@local-agent.test`,
    name: `Visual ${projectName} Directory Member ${suffix}`,
    password,
  };
}

export async function createReferenceParityFixture(
  page: Page,
  organizationScenario: ReferenceParityOrganizationScenario,
  projectName: string,
): Promise<ReferenceParityFixture> {
  const ownerContext = await organizationScenario.createContext(
    `${projectName} visual owner setup`,
  );
  const inviterContext = await organizationScenario.createContext(
    `${projectName} visual invitation setup`,
  );
  const memberContext = await organizationScenario.createContext(
    `${projectName} visual member setup`,
  );
  const run = crypto.randomUUID().replaceAll("-", "");
  const ownerIdentity = identity(
    projectName,
    "owner",
    `E2E-Visual-Owner-${run}!A1`,
  );
  const inviterIdentity = identity(
    projectName,
    "invite-sender",
    `E2E-Visual-Inviter-${run}!A1`,
  );
  const memberIdentity = identity(
    projectName,
    "member",
    `E2E-Visual-Member-${run}!A1`,
  );
  const directoryMemberIdentities = Array.from({ length: 14 }, (_, index) =>
    directoryMemberIdentity(
      projectName,
      index,
      `E2E-Visual-Directory-${index + 1}-${run}!A1`,
    ),
  );
  const directoryMemberContexts = await Promise.all(
    directoryMemberIdentities.map((_, index) =>
      organizationScenario.createContext(
        `${projectName} visual directory member ${index + 1} setup`,
      ),
    ),
  );
  const [owner, inviter, member, ...directoryMembers] =
    await organizationScenario.createLocalUsers([
      organizationScenario.prepareLocalUser(
        ownerContext,
        ownerIdentity,
        `${projectName} visual owner`,
      ),
      organizationScenario.prepareLocalUser(
        inviterContext,
        inviterIdentity,
        `${projectName} visual invitation sender`,
      ),
      organizationScenario.prepareLocalUser(
        memberContext,
        memberIdentity,
        `${projectName} visual member`,
      ),
      ...directoryMemberIdentities.map((directoryIdentity, index) =>
        organizationScenario.prepareLocalUser(
          directoryMemberContexts[index],
          directoryIdentity,
          `${projectName} visual directory member ${index + 1}`,
        ),
      ),
    ]);
  const invitationOrganization = await organizationScenario.createOrganization(
    inviter,
    inviterContext.request,
    `Reference ${projectName} Workspace`,
  );
  const incomingInvitation = await createGeneratedInvitation(
    inviterContext.request,
    invitationOrganization.id,
    { email: ownerIdentity.email, role: "member" },
  );
  let organizationFixture: ReferenceParityOrganizationFixture | undefined;
  let alternateOrganizationCreated = false;

  return {
    async assertSafeScreenshot(targetPage) {
      const bodyText = await targetPage.locator("body").innerText();
      for (const secret of [
        ownerIdentity.password,
        inviterIdentity.password,
        memberIdentity.password,
        ...directoryMemberIdentities.map(({ password }) => password),
      ]) {
        if (bodyText.includes(secret)) {
          throw new Error(
            "Visual screenshot surface disclosed an E2E credential.",
          );
        }
      }
      if (
        /https?:\/\/(?:127\.0\.0\.1|localhost):\d+\/api(?:\/|\b)/u.test(
          bodyText,
        )
      ) {
        throw new Error(
          "Visual screenshot surface disclosed an internal API URL.",
        );
      }
      if (/\b(?:org|user)_[A-Za-z0-9_-]{20,}\b/u.test(bodyText)) {
        throw new Error(
          "Visual screenshot surface disclosed an API credential.",
        );
      }
    },
    invitationId: incomingInvitation.id,
    ownerIdentity,
    async createOrganizationFixture() {
      if (organizationFixture) {
        throw new Error(
          "Visual organization fixture was created more than once.",
        );
      }
      const organization = await organizationScenario.createOrganization(
        owner,
        ownerContext.request,
        `Visual ${projectName} Workspace`,
      );
      await addGeneratedOrganizationMember(
        ownerContext.request,
        organization.id,
        member.user.id,
        "member",
      );
      for (const directoryMember of directoryMembers) {
        await addGeneratedOrganizationMember(
          ownerContext.request,
          organization.id,
          directoryMember.user.id,
          "member",
        );
      }
      const team = await createGeneratedTeam(
        ownerContext.request,
        organization.id,
        `Visual ${projectName} Team`,
      );
      const outgoingInvitation = await createGeneratedInvitation(
        ownerContext.request,
        organization.id,
        {
          email: inviterIdentity.email,
          role: "member",
          teamId: team.id,
        },
      );
      for (let index = 0; index < 14; index += 1) {
        await createGeneratedInvitation(ownerContext.request, organization.id, {
          email: `visual-${safeProjectSlug(projectName)}-pending-${String(index + 1).padStart(2, "0")}@example.test`,
          role: "member",
          teamId: team.id,
        });
      }
      for (let index = 0; index < 15; index += 1) {
        const createdApiKey = await createGeneratedOrganizationKey(
          ownerContext.request,
          organization.id,
          {
            name: `Visual ${projectName} Reader ${String(index + 1).padStart(2, "0")}`,
            presetIds: ["organization-read"],
          },
        );
        createdApiKey.takeCredential();
      }
      organizationFixture = {
        invitation: incomingInvitation,
        organization,
        outgoingInvitation,
        team,
      };
      return organizationFixture;
    },
    async ensureAlternateOrganization() {
      if (!organizationFixture) {
        throw new Error("Visual organization fixture is not ready.");
      }
      if (alternateOrganizationCreated) return;
      await organizationScenario.createOrganization(
        owner,
        ownerContext.request,
        `Visual ${projectName} Alternate Workspace`,
      );
      alternateOrganizationCreated = true;
    },
    pathFixture() {
      if (!organizationFixture) {
        throw new Error("Visual organization fixture is not ready.");
      }
      return {
        invitationId: incomingInvitation.id,
        organizationKey: organizationFixture.organization.canonicalKey,
      };
    },
    async signIn(targetPage) {
      await signInLocalAutomationUser(
        targetPage.context().request,
        ownerIdentity.email,
        ownerIdentity.password,
      );
    },
  };
}
