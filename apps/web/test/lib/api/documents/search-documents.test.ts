/** @jest-environment node */

import { searchDocumentsSystem } from "@/src/lib/api/generated/sdk.gen";
import type { DocumentSearchResponse } from "@/src/lib/api/generated/types.gen";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import { searchDocuments } from "@/src/lib/api/documents/browser/search-documents";

jest.mock("@/src/lib/api/generated/sdk.gen", () => ({
  searchDocumentsSystem: jest.fn(),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: jest.fn(),
}));

const client = { role: "browser" };
const mockedSearchDocumentsSystem = jest.mocked(searchDocumentsSystem);
const mockedCreateBrowserApiClient = jest.mocked(createBrowserApiClient);

const response: DocumentSearchResponse = {
  pages: [
    {
      type: "page",
      title: "API keys",
      description: "Create and manage API keys.",
      href: "/docs/api/api-keys",
      group: "Developers",
      parentItem: "API",
    },
  ],
  headings: [
    {
      type: "heading",
      title: "Create a key",
      href: "/docs/api/api-keys#create-a-key",
      pageTitle: "API keys",
      group: "Developers",
      parentItem: "API keys",
    },
  ],
};

beforeEach(() => {
  jest.clearAllMocks();
  mockedCreateBrowserApiClient.mockReturnValue(client as never);
});

it("searches through the generated browser operation, unwraps its envelope, and propagates abort", async () => {
  const signal = new AbortController().signal;
  mockedSearchDocumentsSystem.mockResolvedValue({
    data: { data: response },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(null, { status: 200 }),
  });

  await expect(
    searchDocuments({ query: "api", locale: "en", signal }),
  ).resolves.toEqual({ ok: true, data: response });

  expect(mockedCreateBrowserApiClient).toHaveBeenCalledTimes(1);
  expect(mockedSearchDocumentsSystem).toHaveBeenCalledWith({
    client: expect.anything(),
    query: { q: "api", locale: "en" },
    signal,
  });
});

it("normalizes generated problem failures without exposing backend detail", async () => {
  mockedSearchDocumentsSystem.mockResolvedValue({
    data: undefined,
    error: {
      type: "urn:template:problem:validation_failed",
      title: "Invalid query",
      status: 400,
      detail: "private document search diagnostic",
      instance: "/api/v1/documents-system/search",
      code: "validation_failed",
      traceId: "trace-documents-search",
      errors: { q: ["The query is invalid."] },
    },
    request: new Request("https://example.test"),
    response: new Response(null, { status: 400 }),
  });

  const result = await searchDocuments({ query: "api", locale: "en" });

  expect(result).toEqual({
    ok: false,
    failure: {
      kind: "problem",
      code: "validation_failed",
      status: 400,
      traceId: "trace-documents-search",
    },
  });
  expect(JSON.stringify(result)).not.toContain(
    "private document search diagnostic",
  );
});

it("normalizes transport exceptions without exposing their message", async () => {
  mockedSearchDocumentsSystem.mockRejectedValue(
    new TypeError("private-document-search-origin"),
  );

  const result = await searchDocuments({ query: "api", locale: "ru" });

  expect(result).toEqual({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });
  expect(JSON.stringify(result)).not.toContain(
    "private-document-search-origin",
  );
});
