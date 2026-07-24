/** @jest-environment node */

import { createServerApiClient } from "@/src/lib/api/server/client";
import { loadServerSystemStatus } from "@/src/lib/api/server/load-server-system-status";

const ORIGINAL_API_INTERNAL_BASE_URL = process.env.API_INTERNAL_BASE_URL;

afterEach(() => {
  if (ORIGINAL_API_INTERNAL_BASE_URL === undefined) {
    delete process.env.API_INTERNAL_BASE_URL;
  } else {
    process.env.API_INTERNAL_BASE_URL = ORIGINAL_API_INTERNAL_BASE_URL;
  }
});

describe("createServerApiClient", () => {
  it("creates isolated clients with only cookie and correlation forwarding", () => {
    process.env.API_INTERNAL_BASE_URL = "http://127.0.0.1:5297/";

    const first = createServerApiClient({
      cookie: "__Host-template.session=first",
      correlationId: "trace-first",
    });
    const second = createServerApiClient({
      cookie: "__Host-template.session=second",
    });
    const anonymous = createServerApiClient();

    expect(first.ok).toBe(true);
    expect(second.ok).toBe(true);
    expect(anonymous.ok).toBe(true);

    if (!first.ok || !second.ok || !anonymous.ok) {
      throw new Error("Expected valid server API clients.");
    }

    const firstHeaders = new Headers(
      first.client.getConfig().headers as HeadersInit | undefined,
    );
    const secondHeaders = new Headers(
      second.client.getConfig().headers as HeadersInit | undefined,
    );
    const anonymousHeaders = new Headers(
      anonymous.client.getConfig().headers as HeadersInit | undefined,
    );

    expect(first.client).not.toBe(second.client);
    expect(firstHeaders.get("cookie")).toBe("__Host-template.session=first");
    expect(firstHeaders.get("x-correlation-id")).toBe("trace-first");
    expect(firstHeaders.get("authorization")).toBeNull();
    expect(secondHeaders.get("cookie")).toBe("__Host-template.session=second");
    expect(secondHeaders.get("x-correlation-id")).toBeNull();
    expect(anonymousHeaders.get("cookie")).toBeNull();
    expect(first.client.getConfig().cache).toBe("no-store");
  });

  it("returns a safe configuration failure without issuing a request", async () => {
    delete process.env.API_INTERNAL_BASE_URL;

    await expect(loadServerSystemStatus()).resolves.toEqual({
      ok: false,
      failure: {
        kind: "configuration",
        code: "api_configuration_missing",
      },
    });
  });
});
