/** @jest-environment node */

import { createBrowserApiClient } from "@/src/lib/api/browser/client";

describe("createBrowserApiClient", () => {
  it("uses a relative base and same-origin credentials", () => {
    const config = createBrowserApiClient().getConfig();

    expect(config.baseUrl).toBe("");
    expect(config.credentials).toBe("same-origin");
    expect(
      new Headers(config.headers as HeadersInit | undefined).has(
        "authorization",
      ),
    ).toBe(false);
  });
});
