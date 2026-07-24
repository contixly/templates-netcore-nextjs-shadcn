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
});
