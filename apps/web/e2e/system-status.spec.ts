import { expect, test } from "@playwright/test";

import { waitForAppHydration } from "./support/app-readiness";

const webOrigin = "http://127.0.0.1:3127";
const browserStatusPath = "/api/v1/system/status";

test("SSR and browser use the API through their supported paths", async ({
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

  const browserRequest = page.waitForRequest((request) => {
    const url = new URL(request.url());
    return (
      url.pathname === browserStatusPath &&
      url.searchParams.get("echo") === "browser"
    );
  });

  await page.goto("/");
  await waitForAppHydration(page);

  const request = await browserRequest;
  expect(new URL(request.url()).origin).toBe(webOrigin);

  const serverRegion = page
    .getByTestId("status-ssr")
    .filter({ hasText: "API is available" });
  await expect(serverRegion).toContainText("API is available");
  await expect(serverRegion).toContainText("API version");
  await expect(serverRegion).toContainText("ssr");

  const browserRegion = page.getByTestId("status-browser");
  await expect(browserRegion).toContainText("API is available");
  await expect(browserRegion).toContainText("API version");
  await expect(browserRegion).toContainText("browser");

  expect(pageErrors).toEqual([]);
  expect(firstPartyServerErrors).toEqual([]);
});

test("browser Problem Details is safe and retry restores success", async ({
  page,
}) => {
  const routePattern = "**/api/v1/system/status**";

  await page.route(routePattern, async (route) => {
    const url = new URL(route.request().url());

    if (url.searchParams.get("echo") !== "browser") {
      await route.continue();
      return;
    }

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
  await waitForAppHydration(page);

  const browserRegion = page.getByTestId("status-browser");
  await expect(browserRegion).toContainText(
    "The API could not complete the request.",
  );
  await expect(browserRegion).toContainText("Trace ID: trace-playwright");
  await expect(browserRegion).not.toContainText("private backend detail");
  await expect(browserRegion).not.toContainText("Invariant internal title");

  await page.unroute(routePattern);
  await browserRegion.getByRole("button", { name: "Retry" }).click();

  await expect(browserRegion).toContainText("API is available");
  await expect(browserRegion).toContainText("browser");
});

test("theme toggle is keyboard accessible", async ({ page }) => {
  await page.goto("/");
  await waitForAppHydration(page);

  const toggle = page.getByRole("button", { name: "Switch to dark theme" });
  await expect(toggle).toBeEnabled();
  await toggle.focus();
  await page.keyboard.press("Enter");

  await expect(page.locator("html")).toHaveClass(/dark/);
});
