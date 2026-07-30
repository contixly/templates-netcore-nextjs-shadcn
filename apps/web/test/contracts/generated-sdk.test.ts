/** @jest-environment node */

import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import {
  addOrganizationMember,
  challengeExternalAuth,
  createOrganization,
  createLocalAutomationScenario,
  deleteAccount,
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
  getOrganizations,
  getSystemStatus,
  logout,
  revokeAccountSession,
  revokeOtherAccountSessions,
  setActiveOrganization,
  signInLocalAutomation,
  updateAccountProfile,
  updateOrganization,
  updateOrganizationMemberRole,
} from "@/src/lib/api/generated";

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
      CreateOrganizationErrors: [400, 401, 403, 405, 409, 500],
      GetOrganizationByKeyErrors: [401, 404, 405, 500],
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
