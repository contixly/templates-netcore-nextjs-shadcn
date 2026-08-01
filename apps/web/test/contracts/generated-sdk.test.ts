/** @jest-environment node */

import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import {
  addOrganizationMember,
  addTeamMember,
  acceptInvitation,
  challengeExternalAuth,
  confirmLocalAutomationEmail,
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
  revokeAccountSession,
  revokeOtherAccountSessions,
  rejectInvitation,
  removeTeamMember,
  setActiveOrganization,
  signInLocalAutomation,
  updateAccountProfile,
  updateOrganization,
  updateOrganizationMemberRole,
  updateTeam,
} from "@/src/lib/api/generated";
import type {
  ApiResponseOfInvitationResponse,
  ConfirmLocalAutomationEmailData,
  CreateInvitationData,
  CreateInvitationErrors,
  CreateInvitationResponses,
  GetOrganizationInvitationsData,
  GetTeamMemberCandidatesData,
} from "@/src/lib/api/generated";

type Assert<T extends true> = T;
type Equal<Left, Right> =
  (<T>() => T extends Left ? 1 : 2) extends
  (<T>() => T extends Right ? 1 : 2)
    ? true
    : false;
type _CreateInvitationBodyIsRequired = Assert<
  Equal<undefined extends CreateInvitationData["body"] ? true : false, false>
>;
type _CreateInvitationTeamIsNullable = Assert<
  null extends NonNullable<CreateInvitationData["body"]>["teamId"] ? true : false
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
  Equal<NonNullable<GetTeamMemberCandidatesData["query"]>["q"], string | undefined>
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

  it("generates the exact organization error unions", () => {
    const generatedTypes = readFileSync(
      resolve(process.cwd(), "src/lib/api/generated/types.gen.ts"),
      "utf8",
    );
    const expected = {
      GetOrganizationsErrors: [400, 401, 405, 500],
      CreateOrganizationErrors: [400, 401, 405, 409, 500],
      GetOrganizationByKeyErrors: [401, 404, 405, 409, 500],
      UpdateOrganizationErrors: [400, 401, 403, 404, 405, 409, 500],
      DeleteOrganizationErrors: [400, 401, 403, 404, 405, 409, 500],
      SetActiveOrganizationErrors: [400, 401, 404, 405, 409, 500],
      GetOrganizationMembersErrors: [400, 401, 404, 405, 409, 500],
      AddOrganizationMemberErrors: [400, 401, 403, 404, 405, 409, 500],
      UpdateOrganizationMemberRoleErrors: [400, 401, 403, 404, 405, 409, 500],
    } as const;

    for (const [typeName, statuses] of Object.entries(expected)) {
      expect(generatedErrorStatuses(generatedTypes, typeName)).toEqual(
        statuses,
      );
    }
  });

  it("generates exact collaboration and local-confirmation error unions", () => {
    const generatedTypes = readFileSync(
      resolve(process.cwd(), "src/lib/api/generated/types.gen.ts"),
      "utf8",
    );
    const standard = [400, 401, 403, 404, 405, 409, 500];
    const rateLimited = [400, 401, 403, 404, 405, 409, 429, 500];
    const expected = {
      GetTeamsErrors: standard,
      CreateTeamErrors: standard,
      UpdateTeamErrors: standard,
      DeleteTeamErrors: standard,
      GetTeamMembersErrors: standard,
      AddTeamMemberErrors: standard,
      RemoveTeamMemberErrors: standard,
      GetTeamMemberCandidatesErrors: standard,
      GetOrganizationInvitationsErrors: standard,
      CreateInvitationErrors: rateLimited,
      GetAccountInvitationsErrors: standard,
      GetInvitationDecisionErrors: standard,
      AcceptInvitationErrors: rateLimited,
      RejectInvitationErrors: rateLimited,
      ConfirmLocalAutomationEmailErrors: [400, 401, 403, 404, 405, 500],
    } as const;

    for (const [typeName, statuses] of Object.entries(expected)) {
      expect(generatedErrorStatuses(generatedTypes, typeName)).toEqual(
        statuses,
      );
    }
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

function generatedErrorStatuses(source: string, typeName: string): number[] {
  const start = source.indexOf(`export type ${typeName} = {`);
  expect(start).toBeGreaterThanOrEqual(0);
  const end = source.indexOf("\n};", start);
  expect(end).toBeGreaterThan(start);
  return [...source.slice(start, end).matchAll(/^[ ]+(\d{3}):/gm)].map(
    (match) => Number(match[1]),
  );
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
