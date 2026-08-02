import { expect, test } from "@playwright/test";

import {
  collectGeneratedPagesToExhaustion,
  credentialCodeNodeIsRemovedOrCleared,
} from "./support/api-key-e2e-harness";
import type { GeneratedApiCall } from "./support/generated-api-keys-api";

type TestItem = Readonly<{ id: string }>;

function successfulPage(
  items: TestItem[],
  nextCursor: null | string,
): GeneratedApiCall<
  Readonly<{ items: TestItem[]; nextCursor: null | string }>
> {
  return {
    cacheControl: "no-store",
    contentType: "application/json",
    data: { items, nextCursor },
    envelopeKeys: ["data"],
    hasSetCookie: false,
    location: null,
    ok: true,
    problemKeys: [],
    status: 200,
  };
}

test("limit-one collection rejects all items returned on the first page", async () => {
  await expect(
    collectGeneratedPagesToExhaustion({
      expectedIds: ["item-1", "item-2"],
      fetchPage: async () =>
        successfulPage([{ id: "item-1" }, { id: "item-2" }], null),
      validateItem: () => undefined,
    }),
  ).rejects.toThrow("Generated limit=1 page did not contain exactly one item.");
});

test("limit-one collection rejects an extra empty terminal page", async () => {
  await expect(
    collectGeneratedPagesToExhaustion({
      expectedIds: ["item-1"],
      fetchPage: async (cursor) =>
        cursor === undefined
          ? successfulPage([{ id: "item-1" }], "cursor-1")
          : successfulPage([], null),
      validateItem: () => undefined,
    }),
  ).rejects.toThrow(
    "Generated pagination did not terminate after the exact expected page count.",
  );
});

test("credential disposal requires a hidden code node to be cleared or removed", async ({
  page,
}) => {
  await page.setContent("<code hidden>credential sentinel</code>");
  const credentialNode = await page
    .getByRole("code", { includeHidden: true })
    .elementHandle();
  if (!credentialNode) {
    throw new Error("Credential code fixture was unavailable.");
  }

  expect(await credentialCodeNodeIsRemovedOrCleared(credentialNode)).toBe(
    false,
  );
  await credentialNode.evaluate((node) => {
    node.textContent = "";
  });
  expect(await credentialCodeNodeIsRemovedOrCleared(credentialNode)).toBe(true);
  await credentialNode.evaluate((node) => node.remove());
  expect(await credentialCodeNodeIsRemovedOrCleared(credentialNode)).toBe(true);
});
