import { expect, test } from "@playwright/test";

const webOrigin = "http://127.0.0.1:3127";

test.describe("anonymous documentation", () => {
  test("public routes expose the locale-neutral documentation shell without authentication", async ({
    page,
  }) => {
    const indexResponse = await page.goto("/docs/index");
    const indexRequest = indexResponse?.request().redirectedFrom();
    const redirectResponse = await indexRequest?.response();

    expect(redirectResponse?.status()).toBe(308);
    await expect(page).toHaveURL(`${webOrigin}/docs`);
    await expect(page.locator('link[rel="canonical"]')).toHaveAttribute(
      "href",
      `${webOrigin}/docs`,
    );

    const rootResponse = await page.goto("/docs");

    expect(rootResponse?.status()).toBe(200);
    await expect(page).toHaveURL(`${webOrigin}/docs`);
    await expect(
      page.getByRole("heading", { level: 1, name: "Template documentation" }),
    ).toBeVisible();
    await expect(page.locator('link[rel="canonical"]')).toHaveAttribute(
      "href",
      `${webOrigin}/docs`,
    );

    const rootSidebar = page.getByRole("navigation", {
      name: "Documentation",
      exact: true,
    });
    await expect(
      rootSidebar.getByRole("link", {
        name: "Template documentation",
        exact: true,
      }),
    ).toHaveAttribute("aria-current", "page");

    const deepResponse = await page.goto("/docs/api/api-v1");

    expect(deepResponse?.status()).toBe(200);
    await expect(page).toHaveURL(`${webOrigin}/docs/api/api-v1`);
    await expect(page).not.toHaveURL(/\/auth\/login/u);
    await expect(
      page.getByRole("heading", { level: 1, name: "API v1 reference" }),
    ).toBeVisible();

    const deepSidebar = page.getByRole("navigation", {
      name: "Documentation",
      exact: true,
    });
    await expect(
      deepSidebar.getByRole("link", {
        name: "API v1 reference",
        exact: true,
      }),
    ).toHaveAttribute("aria-current", "page");

    const pageNavigation = page.getByRole("navigation", {
      name: "Document navigation",
    });
    await expect(pageNavigation).toHaveCount(2);
    await expect(
      pageNavigation.first().getByRole("link", {
        name: "Previous document: Manage API keys",
      }),
    ).toHaveAttribute("href", "/docs/api/api-keys");
    await expect(
      pageNavigation.first().getByRole("link", {
        name: "Next document: Permissions and rate limits",
      }),
    ).toHaveAttribute("href", "/docs/api/permissions-rate-limits");

    const tableOfContents = page.getByRole("navigation", {
      name: "On this page",
    });
    await expect(tableOfContents).toBeVisible();
    await expect(
      tableOfContents.getByRole("link", { name: "Supported reads" }),
    ).toHaveAttribute("href", "#supported-reads");

    const unknownResponse = await page.goto("/docs/private/unknown");

    // Next.js commits the streaming shell before this segment calls notFound(),
    // so the documented transport contract is 200 plus noindex and 404 UI.
    expect(unknownResponse?.status()).toBe(200);
    await expect(page).toHaveURL(`${webOrigin}/docs/private/unknown`);
    await expect(page).not.toHaveURL(/\/auth\/login/u);
    await expect(
      page.getByRole("heading", { level: 1, name: "Page not found" }),
    ).toBeVisible();
    await expect(page.locator('meta[name="robots"]').first()).toHaveAttribute(
      "content",
      /noindex/u,
    );
  });

  test("search shortcut exposes empty navigation and reaches a page heading", async ({
    page,
  }) => {
    await page.goto("/docs");

    // Opening once through the hydrated control gives the global shortcut
    // effect a deterministic black-box readiness boundary.
    await page.getByRole("button", { name: "Search docs" }).click();
    await expect(
      page.getByRole("dialog", { name: "Search docs" }),
    ).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(
      page.getByRole("dialog", { name: "Search docs" }),
    ).not.toBeVisible();

    await page.keyboard.press("ControlOrMeta+K");
    const search = page.getByRole("searchbox", { name: "Search docs" });
    await expect(search).toBeFocused();

    const emptySearchResults = page.getByRole("listbox", {
      name: "Search results",
    });
    await expect(emptySearchResults).toBeVisible();
    await expect(
      emptySearchResults.getByRole("option", {
        name: /Template documentation/u,
      }),
    ).toBeVisible();

    await search.fill("api");
    const apiPageResult = emptySearchResults
      .getByRole("option", { name: /API v1 reference/u })
      .first();
    await expect(apiPageResult).toBeEnabled();
    await apiPageResult.click();
    await expect(page).toHaveURL(`${webOrigin}/docs/api/api-v1`);
    await expect(search).not.toBeVisible();

    await page.keyboard.press("ControlOrMeta+K");
    await search.fill("Supported reads");
    const headingResult = page
      .getByRole("listbox", { name: "Search results" })
      .getByRole("option", {
        name: /Supported reads.*API v1 reference/u,
      });
    await expect(headingResult).toBeEnabled();
    await headingResult.click();

    await expect(page).toHaveURL(
      `${webOrigin}/docs/api/api-v1#supported-reads`,
    );
    await expect(
      page.getByRole("heading", { level: 2, name: /Supported reads/u }),
    ).toBeVisible();
  });

  test("social images return PNG content and reject unknown documents", async ({
    request,
  }) => {
    const image = await request.get("/docs/og/api/api-v1?locale=en");

    expect(image.status()).toBe(200);
    expect(image.headers()["content-type"]?.split(";", 1)[0]).toBe("image/png");
    expect((await image.body()).subarray(0, 8)).toEqual(
      Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    );

    const unknown = await request.get("/docs/og/private/unknown?locale=en");

    expect(unknown.status()).toBe(404);
    expect((await unknown.body()).byteLength).toBe(0);
  });
});
