/** @jest-environment node */

import { createClient } from "@/src/lib/api/generated/client";
import { loadSystemStatus } from "@/src/lib/api/load-system-status";

const successPayload = {
  data: {
    status: "ok",
    apiVersion: "1",
    timestamp: "2026-07-24T00:00:00Z",
    echo: "ssr",
  },
};

describe("loadSystemStatus", () => {
  it("calls the generated operation with no-store and unwraps data", async () => {
    const fetchMock = jest
      .fn<ReturnType<typeof fetch>, Parameters<typeof fetch>>()
      .mockImplementation(async (input) => {
        const request = input as Request;

        expect(request.url).toBe(
          "https://api.example.test/api/v1/system/status?echo=ssr",
        );
        expect(request.cache).toBe("no-store");

        return new Response(JSON.stringify(successPayload), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      });
    const client = createClient({
      baseUrl: "https://api.example.test",
      fetch: fetchMock,
    });

    await expect(loadSystemStatus(client, "ssr")).resolves.toEqual({
      ok: true,
      data: successPayload.data,
    });
  });

  it("keeps only stable problem fields", async () => {
    const fetchMock = jest
      .fn<ReturnType<typeof fetch>, Parameters<typeof fetch>>()
      .mockResolvedValue(
        new Response(
          JSON.stringify({
            type: "https://example.test/internal",
            title: "Invariant internal title",
            status: 500,
            detail: "private backend detail",
            instance: "/api/v1/system/status",
            code: "internal_error",
            traceId: "trace-e2e",
          }),
          {
            status: 500,
            headers: { "content-type": "application/problem+json" },
          },
        ),
      );
    const client = createClient({
      baseUrl: "https://api.example.test",
      fetch: fetchMock,
    });

    const result = await loadSystemStatus(client, "browser");

    expect(result).toEqual({
      ok: false,
      failure: {
        kind: "problem",
        code: "internal_error",
        status: 500,
        traceId: "trace-e2e",
      },
    });
    expect(JSON.stringify(result)).not.toContain("private backend detail");
    expect(JSON.stringify(result)).not.toContain("Invariant internal title");
  });

  it("normalizes transport exceptions without exposing their message", async () => {
    const fetchMock = jest
      .fn<ReturnType<typeof fetch>, Parameters<typeof fetch>>()
      .mockRejectedValue(new TypeError("private-internal-origin"));
    const client = createClient({
      baseUrl: "https://api.example.test",
      fetch: fetchMock,
    });

    const result = await loadSystemStatus(client, "browser");

    expect(result).toEqual({
      ok: false,
      failure: {
        kind: "network",
        code: "api_unavailable",
      },
    });
    expect(JSON.stringify(result)).not.toContain("private-internal-origin");
  });
});
