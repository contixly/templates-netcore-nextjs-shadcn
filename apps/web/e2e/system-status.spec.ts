import { expect, test } from "@playwright/test";

import { waitForInteraction } from "./support/app-readiness";

const webOrigin = "http://127.0.0.1:3127";
test("public landing keeps health diagnostics out of the product while the API remains same-origin", async ({
  page,
}) => {
  const pageErrors: string[] = [];
  const firstPartyServerErrors: string[] = [];
  page.on("pageerror", (error) => pageErrors.push(error.message));
  page.on("response", (response) => {
    const url = new URL(response.url());
    if (url.origin === webOrigin && response.status() >= 500) {
      firstPartyServerErrors.push(`${response.status()} ${url.pathname}`);
    }
  });

  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "Build the product, not the plumbing" }),
  ).toBeVisible();
  await expect(page.getByTestId("status-ssr")).toHaveCount(0);
  await expect(page.getByTestId("status-browser")).toHaveCount(0);

  const response = await page.request.get(
    `${webOrigin}/api/v1/system/status?echo=browser`,
  );
  expect(response.status()).toBe(200);
  expect(new URL(response.url()).origin).toBe(webOrigin);
  await expect(response.json()).resolves.toMatchObject({
    data: { echo: "browser" },
  });

  expect(pageErrors).toEqual([]);
  expect(firstPartyServerErrors).toEqual([]);
});

test("public landing never mounts diagnostic Problem Details content", async ({
  page,
}) => {
  const routePattern = "**/api/v1/system/status**";
  let diagnosticRequested = false;

  await page.route(routePattern, async (route) => {
    diagnosticRequested = true;
    await route.fulfill({
      status: 500,
      contentType: "application/problem+json",
      body: JSON.stringify({
        type: "https://example.test/internal",
        title: "Invariant internal title",
        status: 500,
        detail: "private backend detail",
        instance: "/api/v1/system/status",
        code: "internal_error",
        traceId: "trace-playwright",
      }),
    });
  });

  await page.goto("/");
  expect(diagnosticRequested).toBe(false);
  await expect(page.locator("body")).not.toContainText(
    "The API could not complete the request.",
  );
  await expect(page.locator("body")).not.toContainText("trace-playwright");
  await expect(page.locator("body")).not.toContainText(
    "private backend detail",
  );
});

test("theme toggle is keyboard accessible", async ({ page }) => {
  await page.goto("/");

  const toggle = page.getByRole("button", { name: "Switch to dark theme" });
  await waitForInteraction(toggle);
  await toggle.focus();
  await page.keyboard.press("Enter");

  await expect(page.locator("html")).toHaveClass(/dark/);
});
