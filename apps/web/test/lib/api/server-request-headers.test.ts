/** @jest-environment node */

const headersMock = jest.fn();

jest.mock("next/headers", () => ({
  headers: () => headersMock(),
}));

import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

it("forwards only Cookie and correlation ID", async () => {
  headersMock.mockResolvedValue(
    new Headers({
      cookie: "__Host-template.session=opaque",
      authorization: "Bearer must-not-forward",
      "x-correlation-id": "trace-auth",
      "x-extra": "must-not-forward",
    }),
  );

  await expect(readForwardedApiHeaders()).resolves.toEqual({
    cookie: "__Host-template.session=opaque",
    correlationId: "trace-auth",
  });
});
