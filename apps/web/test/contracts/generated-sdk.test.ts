/** @jest-environment node */

import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import {
  addOrganizationMember,
  addTeamMember,
  acceptInvitation,
  challengeExternalAuth,
  confirmLocalAutomationEmail,
  createOrganizationApiKey,
  createPersonalApiKey,
  createTeam,
  createOrganization,
  createLocalAutomationScenario,
  createInvitation,
  deleteAccount,
  deleteTeam,
  deleteOrganization,
  deleteLocalAutomationScenario,
  disconnectAccountProvider,
  getAccount,
  getAccountConnections,
  getAccountSessions,
  getAuthCapabilities,
  getAuthCsrf,
  getAuthSession,
  getOrganizationByKey,
  getApiKeyPrincipal,
  getMachineOrganization,
  getOrganizationMembers,
  getOrganizationInvitations,
  getAccountInvitations,
  getInvitationDecision,
  getOrganizations,
  getSystemStatus,
  getTeamMemberCandidates,
  getTeamMembers,
  getTeams,
  logout,
  listOrganizationApiKeys,
  listPersonalApiKeys,
  revokeAccountSession,
  revokeOtherAccountSessions,
  revokeOrganizationApiKey,
  revokePersonalApiKey,
  rejectInvitation,
  removeTeamMember,
  setActiveOrganization,
  signInLocalAutomation,
  rotateOrganizationApiKey,
  rotatePersonalApiKey,
  updateAccountProfile,
  updateOrganization,
  updateOrganizationApiKey,
  updateOrganizationMemberRole,
  updateTeam,
  updatePersonalApiKey,
} from "@/src/lib/api/generated";
import type {
  ApiResponseOfInvitationResponse,
  ApiKeyMePrincipalResponse,
  ApiKeyResponse,
  ApiKeySecretResponse,
  AcceptInvitationErrors,
  AddOrganizationMemberErrors,
  AddTeamMemberErrors,
  ConfirmLocalAutomationEmailData,
  ConfirmLocalAutomationEmailErrors,
  CreateInvitationErrors,
  CreateInvitationData,
  CreateInvitationResponses,
  CreateOrganizationApiKeyData,
  CreateOrganizationApiKeyErrors,
  CreateOrganizationErrors,
  CreatePersonalApiKeyData,
  CreatePersonalApiKeyErrors,
  CreateTeamErrors,
  DeleteOrganizationErrors,
  DeleteTeamErrors,
  GetAccountInvitationsErrors,
  GetInvitationDecisionErrors,
  GetApiKeyPrincipalErrors,
  GetMachineOrganizationErrors,
  GetOrganizationByKeyErrors,
  GetOrganizationInvitationsData,
  GetOrganizationInvitationsErrors,
  GetOrganizationMembersErrors,
  GetOrganizationsErrors,
  GetTeamMemberCandidatesErrors,
  GetTeamMembersErrors,
  GetTeamsErrors,
  InvitationResponse,
  ListOrganizationApiKeysData,
  ListOrganizationApiKeysErrors,
  ListPersonalApiKeysErrors,
  RejectInvitationErrors,
  RemoveTeamMemberErrors,
  RevokeOrganizationApiKeyData,
  RevokeOrganizationApiKeyErrors,
  RevokePersonalApiKeyErrors,
  RotateOrganizationApiKeyErrors,
  RotateOrganizationApiKeyData,
  RotatePersonalApiKeyErrors,
  SetActiveOrganizationErrors,
  GetTeamMemberCandidatesData,
  UpdateOrganizationErrors,
  UpdateOrganizationApiKeyData,
  UpdateOrganizationApiKeyErrors,
  UpdateOrganizationMemberRoleErrors,
  UpdateTeamErrors,
  UpdatePersonalApiKeyErrors,
  MachineOrganizationDetailResponse,
  OrganizationSummaryResponse,
} from "@/src/lib/api/generated";

type Assert<T extends true> = T;
type Equal<Left, Right> =
  (<T>() => T extends Left ? 1 : 2) extends <T>() => T extends Right ? 1 : 2
    ? true
    : false;
/* eslint-disable @typescript-eslint/no-unused-vars -- Type aliases below are compile-time SDK contract assertions. */
type _CreateInvitationBodyIsRequired = Assert<
  Equal<undefined extends CreateInvitationData["body"] ? true : false, false>
>;
type _CreateInvitationTeamIsNullable = Assert<
  null extends NonNullable<CreateInvitationData["body"]>["teamId"]
    ? true
    : false
>;
type _CreateInvitationCsrfIsRequired = Assert<
  Equal<
    CreateInvitationData["headers"]["X-CSRF-TOKEN"] extends string
      ? true
      : false,
    true
  >
>;
type _InvitationStatusIsExact = Assert<
  Equal<
    NonNullable<GetOrganizationInvitationsData["query"]>["status"],
    "pending" | "accepted" | "rejected" | "canceled" | "expired" | undefined
  >
>;
type _CandidateQueryIsOptional = Assert<
  Equal<
    NonNullable<GetTeamMemberCandidatesData["query"]>["q"],
    string | undefined
  >
>;
type _CreateInvitationSuccessEnvelopeIsExact = Assert<
  Equal<CreateInvitationResponses[201], ApiResponseOfInvitationResponse>
>;
type _CreateInvitationRateErrorIsExposed = Assert<
  Equal<CreateInvitationErrors[429] extends object ? true : false, true>
>;
type _ConfirmUsesCsrfWithoutBody = Assert<
  Equal<ConfirmLocalAutomationEmailData["body"], never | undefined>
>;
type ErrorStatuses<Error> = keyof Error;
type StandardCollaborationErrors = 400 | 401 | 403 | 404 | 405 | 409 | 500;
type RateLimitedCollaborationErrors = StandardCollaborationErrors | 429;
type _OrganizationErrorStatuses = [
  Assert<
    Equal<
      ErrorStatuses<GetOrganizationsErrors>,
      400 | 401 | 403 | 405 | 429 | 500
    >
  >,
  Assert<
    Equal<ErrorStatuses<CreateOrganizationErrors>, 400 | 401 | 405 | 409 | 500>
  >,
  Assert<
    Equal<
      ErrorStatuses<GetOrganizationByKeyErrors>,
      401 | 404 | 405 | 409 | 500
    >
  >,
  Assert<
    Equal<ErrorStatuses<UpdateOrganizationErrors>, StandardCollaborationErrors>
  >,
  Assert<
    Equal<ErrorStatuses<DeleteOrganizationErrors>, StandardCollaborationErrors>
  >,
  Assert<
    Equal<
      ErrorStatuses<SetActiveOrganizationErrors>,
      400 | 401 | 404 | 405 | 409 | 500
    >
  >,
  Assert<
    Equal<
      ErrorStatuses<GetOrganizationMembersErrors>,
      400 | 401 | 403 | 404 | 405 | 409 | 429 | 500
    >
  >,
  Assert<
    Equal<
      ErrorStatuses<AddOrganizationMemberErrors>,
      StandardCollaborationErrors
    >
  >,
  Assert<
    Equal<
      ErrorStatuses<UpdateOrganizationMemberRoleErrors>,
      StandardCollaborationErrors
    >
  >,
];
type _CollaborationErrorStatuses = [
  Assert<
    Equal<
      InvitationResponse["warning"],
      "notification_failed" | null | undefined
    >
  >,
  Assert<
    Equal<ErrorStatuses<GetTeamsErrors>, StandardCollaborationErrors | 429>
  >,
  Assert<Equal<ErrorStatuses<CreateTeamErrors>, StandardCollaborationErrors>>,
  Assert<Equal<ErrorStatuses<UpdateTeamErrors>, StandardCollaborationErrors>>,
  Assert<Equal<ErrorStatuses<DeleteTeamErrors>, StandardCollaborationErrors>>,
  Assert<
    Equal<
      ErrorStatuses<GetTeamMembersErrors>,
      StandardCollaborationErrors | 429
    >
  >,
  Assert<
    Equal<ErrorStatuses<AddTeamMemberErrors>, StandardCollaborationErrors>
  >,
  Assert<
    Equal<ErrorStatuses<RemoveTeamMemberErrors>, StandardCollaborationErrors>
  >,
  Assert<
    Equal<
      ErrorStatuses<GetTeamMemberCandidatesErrors>,
      StandardCollaborationErrors
    >
  >,
  Assert<
    Equal<
      ErrorStatuses<GetOrganizationInvitationsErrors>,
      StandardCollaborationErrors
    >
  >,
  Assert<
    Equal<ErrorStatuses<CreateInvitationErrors>, RateLimitedCollaborationErrors>
  >,
  Assert<
    Equal<
      ErrorStatuses<GetAccountInvitationsErrors>,
      StandardCollaborationErrors
    >
  >,
  Assert<
    Equal<
      ErrorStatuses<GetInvitationDecisionErrors>,
      StandardCollaborationErrors
    >
  >,
  Assert<
    Equal<ErrorStatuses<AcceptInvitationErrors>, RateLimitedCollaborationErrors>
  >,
  Assert<
    Equal<ErrorStatuses<RejectInvitationErrors>, RateLimitedCollaborationErrors>
  >,
  Assert<
    Equal<
      ErrorStatuses<ConfirmLocalAutomationEmailErrors>,
      400 | 401 | 403 | 404 | 405 | 500
    >
  >,
];

type _ApiKeyErrorStatuses = [
  Assert<
    Equal<ErrorStatuses<GetApiKeyPrincipalErrors>, 401 | 403 | 405 | 429 | 500>
  >,
  Assert<
    Equal<
      ErrorStatuses<GetMachineOrganizationErrors>,
      400 | 401 | 403 | 404 | 405 | 429 | 500
    >
  >,
  Assert<
    Equal<ErrorStatuses<ListPersonalApiKeysErrors>, 400 | 401 | 405 | 500>
  >,
  Assert<
    Equal<
      ErrorStatuses<CreatePersonalApiKeyErrors>,
      400 | 401 | 405 | 409 | 500
    >
  >,
  Assert<
    Equal<
      ErrorStatuses<UpdatePersonalApiKeyErrors>,
      400 | 401 | 404 | 405 | 409 | 500
    >
  >,
  Assert<
    Equal<
      ErrorStatuses<RevokePersonalApiKeyErrors>,
      400 | 401 | 404 | 405 | 409 | 500
    >
  >,
  Assert<
    Equal<
      ErrorStatuses<RotatePersonalApiKeyErrors>,
      400 | 401 | 404 | 405 | 409 | 500
    >
  >,
  Assert<
    Equal<
      ErrorStatuses<ListOrganizationApiKeysErrors>,
      StandardCollaborationErrors
    >
  >,
  Assert<
    Equal<
      ErrorStatuses<CreateOrganizationApiKeyErrors>,
      StandardCollaborationErrors
    >
  >,
  Assert<
    Equal<
      ErrorStatuses<UpdateOrganizationApiKeyErrors>,
      StandardCollaborationErrors
    >
  >,
  Assert<
    Equal<
      ErrorStatuses<RevokeOrganizationApiKeyErrors>,
      StandardCollaborationErrors
    >
  >,
  Assert<
    Equal<
      ErrorStatuses<RotateOrganizationApiKeyErrors>,
      StandardCollaborationErrors
    >
  >,
];

type _ApiKeyCreateBodyIsRequired = Assert<
  Equal<
    undefined extends CreatePersonalApiKeyData["body"] ? true : false,
    false
  >
>;
type _ApiKeySecretIsRevealOnceString = Assert<
  Equal<ApiKeySecretResponse["key"], string>
>;
type _OrganizationApiKeyPathsAreRequired = [
  Assert<
    Equal<ListOrganizationApiKeysData["path"], { organizationId: string }>
  >,
  Assert<
    Equal<CreateOrganizationApiKeyData["path"], { organizationId: string }>
  >,
  Assert<
    Equal<
      UpdateOrganizationApiKeyData["path"],
      { organizationId: string; apiKeyId: string }
    >
  >,
  Assert<
    Equal<
      RevokeOrganizationApiKeyData["path"],
      { organizationId: string; apiKeyId: string }
    >
  >,
  Assert<
    Equal<
      RotateOrganizationApiKeyData["path"],
      { organizationId: string; apiKeyId: string }
    >
  >,
  Assert<
    Equal<
      undefined extends Parameters<typeof listOrganizationApiKeys>[0]
        ? true
        : false,
      false
    >
  >,
];
type _ApiKeyCountersAreNumbers = [
  Assert<Equal<ApiKeyResponse["rateLimitMax"], number>>,
  Assert<Equal<ApiKeyResponse["requestCount"], number>>,
  Assert<Equal<ApiKeySecretResponse["rateLimitMax"], number>>,
  Assert<Equal<ApiKeySecretResponse["requestCount"], number>>,
];
type ApiKeyUserPrincipal = Extract<
  ApiKeyMePrincipalResponse,
  { ownerKind: "user" }
>;
type ApiKeyOrganizationPrincipal = Extract<
  ApiKeyMePrincipalResponse,
  { ownerKind: "organization" }
>;
type OrganizationSummaryUser = Extract<
  OrganizationSummaryResponse,
  { accessPrincipal: "user" }
>;
type OrganizationSummaryMachine = Extract<
  OrganizationSummaryResponse,
  { accessPrincipal: "organization" }
>;
type MachineOrganizationUser = Extract<
  MachineOrganizationDetailResponse,
  { accessPrincipal: "user" }
>;
type MachineOrganizationOwner = Extract<
  MachineOrganizationDetailResponse,
  { accessPrincipal: "organization" }
>;
type _ApiKeyPrincipalNarrowing = [
  Assert<Equal<ApiKeyUserPrincipal["userId"], string>>,
  Assert<Equal<ApiKeyUserPrincipal["organizationId"], null>>,
  Assert<Equal<ApiKeyOrganizationPrincipal["userId"], null>>,
  Assert<Equal<ApiKeyOrganizationPrincipal["organizationId"], string>>,
];
type _OrganizationPrincipalNarrowing = [
  Assert<
    Equal<OrganizationSummaryUser["currentRole"], "member" | "admin" | "owner">
  >,
  Assert<
    Equal<OrganizationSummaryUser["capabilities"]["canManageApiKeys"], boolean>
  >,
  Assert<Equal<OrganizationSummaryMachine["currentRole"], "organization">>,
  Assert<
    Equal<OrganizationSummaryMachine["capabilities"]["canManageApiKeys"], false>
  >,
  Assert<
    Equal<MachineOrganizationUser["currentRole"], "member" | "admin" | "owner">
  >,
  Assert<Equal<MachineOrganizationOwner["currentRole"], "organization">>,
  Assert<
    Equal<
      MachineOrganizationOwner["capabilities"]["canUpdateOrganization"],
      false
    >
  >,
  Assert<
    Equal<
      MachineOrganizationOwner["capabilities"]["canDeleteOrganization"],
      false
    >
  >,
  Assert<
    Equal<MachineOrganizationOwner["capabilities"]["canAddMembers"], false>
  >,
  Assert<
    Equal<
      MachineOrganizationOwner["capabilities"]["canUpdateMemberRoles"],
      false
    >
  >,
  Assert<
    Equal<MachineOrganizationOwner["capabilities"]["canManageTeams"], false>
  >,
  Assert<
    Equal<
      MachineOrganizationOwner["capabilities"]["canManageInvitations"],
      false
    >
  >,
  Assert<
    Equal<MachineOrganizationOwner["capabilities"]["canManageApiKeys"], false>
  >,
];
/* eslint-enable @typescript-eslint/no-unused-vars */

describe("generated system status SDK", () => {
  it("tracks the committed GetSystemStatus operation", () => {
    const contract = JSON.parse(
      readFileSync(
        resolve(process.cwd(), "../../contracts/openapi/v1.json"),
        "utf8",
      ),
    ) as {
      paths: {
        "/api/v1/system/status": {
          get: {
            operationId: string;
          };
        };
      };
    };

    expect(contract.paths["/api/v1/system/status"].get.operationId).toBe(
      "GetSystemStatus",
    );
    expect(getSystemStatus).toEqual(expect.any(Function));
  });

  it("tracks every collaboration and local-confirmation operation", () => {
    expect(getTeams).toEqual(expect.any(Function));
    expect(createTeam).toEqual(expect.any(Function));
    expect(updateTeam).toEqual(expect.any(Function));
    expect(deleteTeam).toEqual(expect.any(Function));
    expect(getTeamMembers).toEqual(expect.any(Function));
    expect(addTeamMember).toEqual(expect.any(Function));
    expect(removeTeamMember).toEqual(expect.any(Function));
    expect(getTeamMemberCandidates).toEqual(expect.any(Function));
    expect(getOrganizationInvitations).toEqual(expect.any(Function));
    expect(createInvitation).toEqual(expect.any(Function));
    expect(getAccountInvitations).toEqual(expect.any(Function));
    expect(getInvitationDecision).toEqual(expect.any(Function));
    expect(acceptInvitation).toEqual(expect.any(Function));
    expect(rejectInvitation).toEqual(expect.any(Function));
    expect(confirmLocalAutomationEmail).toEqual(expect.any(Function));
  });

  it("preserves the local-confirmation production-safety documentation", () => {
    const sdk = readFileSync(
      resolve(process.cwd(), "src/lib/api/generated/sdk.gen.ts"),
      "utf8",
    );
    expect(sdk).toContain(
      "Development/Test only; requires LocalAutomationAuth enabled. Production returns 404. This is not production account verification.",
    );
  });

  it("tracks every iteration-3 auth operation", () => {
    expect(getAuthCapabilities).toEqual(expect.any(Function));
    expect(getAuthSession).toEqual(expect.any(Function));
    expect(getAuthCsrf).toEqual(expect.any(Function));
    expect(logout).toEqual(expect.any(Function));
    expect(createLocalAutomationScenario).toEqual(expect.any(Function));
    expect(signInLocalAutomation).toEqual(expect.any(Function));
    expect(deleteLocalAutomationScenario).toEqual(expect.any(Function));
  });

  it("tracks the external challenge and all eight account operations", () => {
    expect(challengeExternalAuth).toEqual(expect.any(Function));
    expect(getAccount).toEqual(expect.any(Function));
    expect(updateAccountProfile).toEqual(expect.any(Function));
    expect(getAccountConnections).toEqual(expect.any(Function));
    expect(disconnectAccountProvider).toEqual(expect.any(Function));
    expect(getAccountSessions).toEqual(expect.any(Function));
    expect(revokeAccountSession).toEqual(expect.any(Function));
    expect(revokeOtherAccountSessions).toEqual(expect.any(Function));
    expect(deleteAccount).toEqual(expect.any(Function));
  });

  it("tracks all nine organization and membership operations", () => {
    expect(getOrganizations).toEqual(expect.any(Function));
    expect(createOrganization).toEqual(expect.any(Function));
    expect(getOrganizationByKey).toEqual(expect.any(Function));
    expect(updateOrganization).toEqual(expect.any(Function));
    expect(deleteOrganization).toEqual(expect.any(Function));
    expect(setActiveOrganization).toEqual(expect.any(Function));
    expect(getOrganizationMembers).toEqual(expect.any(Function));
    expect(addOrganizationMember).toEqual(expect.any(Function));
    expect(updateOrganizationMemberRole).toEqual(expect.any(Function));
  });

  it("tracks all API key management and machine-only operations", () => {
    expect(listPersonalApiKeys).toEqual(expect.any(Function));
    expect(createPersonalApiKey).toEqual(expect.any(Function));
    expect(updatePersonalApiKey).toEqual(expect.any(Function));
    expect(revokePersonalApiKey).toEqual(expect.any(Function));
    expect(rotatePersonalApiKey).toEqual(expect.any(Function));
    expect(listOrganizationApiKeys).toEqual(expect.any(Function));
    expect(createOrganizationApiKey).toEqual(expect.any(Function));
    expect(updateOrganizationApiKey).toEqual(expect.any(Function));
    expect(revokeOrganizationApiKey).toEqual(expect.any(Function));
    expect(rotateOrganizationApiKey).toEqual(expect.any(Function));
    expect(getApiKeyPrincipal).toEqual(expect.any(Function));
    expect(getMachineOrganization).toEqual(expect.any(Function));
  });

  it("proves generated error unions through compile-time equality", () => {
    const validCreate: CreateInvitationData = {
      body: { email: "invitee@example.test", role: "member", teamId: null },
      headers: { "X-CSRF-TOKEN": "csrf" },
      path: { organizationId: "01900000-0000-7000-8000-000000000010" },
      url: "/api/v1/organizations/{organizationId}/invitations",
    };
    const validConfirm: ConfirmLocalAutomationEmailData = {
      headers: { "X-CSRF-TOKEN": "csrf" },
      url: "/api/local-auth/confirm-email",
    };
    // @ts-expect-error mutations cannot omit the CSRF request token
    const missingCsrf: ConfirmLocalAutomationEmailData = {
      url: "/api/local-auth/confirm-email",
    };
    const invalidStatus: GetOrganizationInvitationsData = {
      path: { organizationId: "01900000-0000-7000-8000-000000000010" },
      query: {
        // @ts-expect-error the filter is a closed display-state union
        status: "unknown",
      },
      url: "/api/v1/organizations/{organizationId}/invitations",
    };
    const confirmWithBody: ConfirmLocalAutomationEmailData = {
      // @ts-expect-error confirmation accepts no request body
      body: {},
      headers: { "X-CSRF-TOKEN": "csrf" },
      url: "/api/local-auth/confirm-email",
    };
    void [
      validCreate,
      validConfirm,
      missingCsrf,
      invalidStatus,
      confirmWithBody,
    ];
    expect(true).toBe(true);
  });

  it("keeps protocol callbacks and provider secrets outside the UI contract", () => {
    const contractText = readFileSync(
      resolve(process.cwd(), "../../contracts/openapi/v1.json"),
      "utf8",
    );
    const generatedText = ["sdk.gen.ts", "types.gen.ts"]
      .map((file) =>
        readFileSync(
          resolve(process.cwd(), "src/lib/api/generated", file),
          "utf8",
        ),
      )
      .join("\n");

    for (const forbidden of [
      "/api/auth/callback/",
      "/api/auth/oauth2/callback/",
      "clientId",
      "clientSecret",
      "accessToken",
      "refreshToken",
      "providerSubject",
      "accounts.google.com",
      "github.com/login/oauth",
      "gitlab.com/oauth",
      "id.vk.com",
      "oauth.yandex",
    ]) {
      expect(contractText).not.toContain(forbidden);
      expect(generatedText).not.toContain(forbidden);
    }
  });

  it("locks auth request-body parity and unsafe 400 response variants", () => {
    const contract = JSON.parse(
      readFileSync(
        resolve(process.cwd(), "../../contracts/openapi/v1.json"),
        "utf8",
      ),
    ) as {
      components: {
        schemas: {
          LocalAutomationSignInRequest: {
            required: string[];
            properties: Record<string, { type: string | string[] }>;
          };
        };
      };
      paths: Record<
        string,
        Record<
          string,
          {
            requestBody?: {
              required?: boolean;
            };
            responses: {
              "400": {
                content: {
                  "application/problem+json": {
                    schema: {
                      $ref?: string;
                      oneOf?: Array<{ $ref: string }>;
                    };
                  };
                };
              };
            };
          }
        >
      >;
    };
    const credentials =
      contract.components.schemas.LocalAutomationSignInRequest;

    expect(
      contract.paths["/api/local-auth/scenario"].post.requestBody?.required,
    ).not.toBe(true);
    expect(
      contract.paths["/api/local-auth/sign-in"].post.requestBody?.required,
    ).toBe(true);
    expect(credentials.required).toEqual(
      expect.arrayContaining(["email", "password"]),
    );
    expect(schemaTypes(credentials.properties.email)).not.toContain("null");
    expect(schemaTypes(credentials.properties.password)).not.toContain("null");
    expect(badRequestSchema(contract, "/api/v1/auth/logout", "post")).toEqual({
      $ref: "#/components/schemas/ProblemDetails",
    });
    expect(
      badRequestSchema(contract, "/api/local-auth/scenario", "delete"),
    ).toEqual({ $ref: "#/components/schemas/ProblemDetails" });
    for (const [path, method] of [
      ["/api/local-auth/scenario", "post"],
      ["/api/local-auth/sign-in", "post"],
    ] as const) {
      expect(
        badRequestSchema(contract, path, method).oneOf?.map(
          (schema) => schema.$ref,
        ),
      ).toEqual([
        "#/components/schemas/ProblemDetails",
        "#/components/schemas/HttpValidationProblemDetails",
      ]);
    }

    const generatedTypes = readFileSync(
      resolve(process.cwd(), "src/lib/api/generated/types.gen.ts"),
      "utf8",
    );
    expect(generatedTypes).toContain(
      "body?: CreateLocalAutomationScenarioRequest;",
    );
    expect(generatedTypes).toContain("body: LocalAutomationSignInRequest;");
    expect(generatedTypes).toContain(
      "400: ProblemDetails | HttpValidationProblemDetails;",
    );
  });
});

function schemaTypes(schema: { type: string | string[] }): string[] {
  return Array.isArray(schema.type) ? schema.type : [schema.type];
}

function badRequestSchema(
  contract: {
    paths: Record<
      string,
      Record<
        string,
        {
          responses: {
            "400": {
              content: {
                "application/problem+json": {
                  schema: {
                    $ref?: string;
                    oneOf?: Array<{ $ref: string }>;
                  };
                };
              };
            };
          };
        }
      >
    >;
  },
  path: string,
  method: string,
) {
  return contract.paths[path][method].responses["400"].content[
    "application/problem+json"
  ].schema;
}
