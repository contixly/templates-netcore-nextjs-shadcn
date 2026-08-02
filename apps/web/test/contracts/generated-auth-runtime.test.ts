/** @jest-environment node */

import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import { createClient } from "@/src/lib/api/generated/client";
import { getApiKeyPrincipal, getOrganizations } from "@/src/lib/api/generated";

const scalarAuthError =
  "Scalar auth cannot be used with alternative security schemes; use a scheme-selective callback or explicit header.";
const secret = "user_secret_must_not_reach_cookie";
type FetchMock = jest.Mock<
  Promise<Response>,
  [input: RequestInfo | URL, init?: RequestInit]
>;

function successfulFetch(): FetchMock {
  return jest.fn<Promise<Response>, Parameters<typeof fetch>>(
    async () =>
      new Response("{}", {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
  );
}

function sentRequest(fetchMock: FetchMock) {
  expect(fetchMock).toHaveBeenCalledTimes(1);
  return fetchMock.mock.calls[0]![0] as Request;
}

it("keeps cookie and API-key authentication as distinct OpenAPI alternatives", () => {
  const document = JSON.parse(
    readFileSync(
      resolve(process.cwd(), "../../contracts/openapi/v1.json"),
      "utf8",
    ),
  ) as {
    paths: {
      "/api/v1/organizations": {
        get: { security: Array<Record<string, unknown>> };
      };
    };
  };

  expect(document.paths["/api/v1/organizations"].get.security).toEqual([
    { cookieAuth: [] },
    { apiKeyAuth: [] },
  ]);
});

it("rejects scalar auth for alternative schemes before sending a request", async () => {
  const fetchMock = successfulFetch();
  const client = createClient({
    auth: secret,
    baseUrl: "https://example.test",
    fetch: fetchMock,
    throwOnError: true,
  });

  const failure: unknown = await getOrganizations({ client }).then(
    () => null,
    (error: unknown) => error,
  );

  expect(failure).toBeInstanceOf(Error);
  expect((failure as Error).message).toBe(scalarAuthError);
  expect((failure as Error).message).not.toContain(secret);
  expect(fetchMock).not.toHaveBeenCalled();
});

it("supports a scheme-selective callback for an alternative API-key scheme", async () => {
  const fetchMock = successfulFetch();
  const client = createClient({
    auth: (auth) => (auth.name === "x-api-key" ? secret : undefined),
    baseUrl: "https://example.test",
    fetch: fetchMock,
    throwOnError: true,
  });

  await getOrganizations({ client });

  const request = sentRequest(fetchMock);
  expect(request.headers.get("x-api-key")).toBe(secret);
  expect(request.headers.has("Cookie")).toBe(false);
});

it("does not reject an empty scalar for alternative schemes", async () => {
  const fetchMock = successfulFetch();
  const client = createClient({
    auth: "",
    baseUrl: "https://example.test",
    fetch: fetchMock,
    throwOnError: true,
  });

  await getOrganizations({ client });

  const request = sentRequest(fetchMock);
  expect(request.headers.has("x-api-key")).toBe(false);
  expect(request.headers.has("Cookie")).toBe(false);
});

it("supports an explicit API-key header without generated auth", async () => {
  const fetchMock = successfulFetch();
  const client = createClient({
    baseUrl: "https://example.test",
    fetch: fetchMock,
    headers: { "x-api-key": secret },
    throwOnError: true,
  });

  await getOrganizations({ client });

  const request = sentRequest(fetchMock);
  expect(request.headers.get("x-api-key")).toBe(secret);
  expect(request.headers.has("Cookie")).toBe(false);
});

it("keeps scalar auth supported for a single API-key scheme", async () => {
  const fetchMock = successfulFetch();
  const client = createClient({
    auth: secret,
    baseUrl: "https://example.test",
    fetch: fetchMock,
    throwOnError: true,
  });

  await getApiKeyPrincipal({ client });

  const request = sentRequest(fetchMock);
  expect(request.headers.get("x-api-key")).toBe(secret);
  expect(request.headers.has("Cookie")).toBe(false);
});
