import { resolveApiBaseUrl } from "@/src/lib/api/api-base-url";

describe("resolveApiBaseUrl", () => {
  it("normalizes an absolute HTTP(S) origin", () => {
    expect(resolveApiBaseUrl("http://127.0.0.1:5297/")).toEqual({
      ok: true,
      value: "http://127.0.0.1:5297",
    });
    expect(resolveApiBaseUrl("https://api.example.test")).toEqual({
      ok: true,
      value: "https://api.example.test",
    });
  });

  it("classifies absent configuration", () => {
    expect(resolveApiBaseUrl(undefined)).toEqual({
      ok: false,
      code: "api_configuration_missing",
    });
  });

  it.each([
    "/api",
    "ftp://api.example.test",
    "https://user:secret@api.example.test",
    "https://api.example.test/base",
    "https://api.example.test?secret=value",
    "https://api.example.test/#fragment",
  ])("rejects non-origin value %s", (value) => {
    expect(resolveApiBaseUrl(value)).toEqual({
      ok: false,
      code: "api_configuration_invalid",
    });
  });
});
