/** @jest-environment node */

import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import {
  createLocalAutomationScenario,
  deleteLocalAutomationScenario,
  getAuthCapabilities,
  getAuthCsrf,
  getAuthSession,
  getSystemStatus,
  logout,
  signInLocalAutomation,
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
