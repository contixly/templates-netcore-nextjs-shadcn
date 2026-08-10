import type { Page, TestInfo } from "@playwright/test";

import { waitForApplicationShell } from "./support/app-readiness";
import {
  buildReferenceParityExpectedPath,
  buildReferenceParityPath,
  createReferenceParityFixture,
  referenceParityRoutes,
  referenceParityRussianOverflowRouteIds,
  type ReferenceParityFixture,
  type ReferenceParityOrganizationFixture,
  type ReferenceParityPathFixture,
  type ReferenceParityRoute,
} from "./support/ui-reference-parity";
import { expect, test } from "./support/organization-test-fixture";

type ScreenshotLocale = "en" | "ru";

const canonicalScreenshotFontStyle = `
  :root {
    --font-sans: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif !important;
    --font-mono: var(--font-sans) !important;
  }
  body {
    font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif !important;
  }
`;

function russianBaseURL(testInfo: TestInfo): string {
  const value = testInfo.project.metadata.russianBaseURL;
  if (typeof value !== "string" || !URL.canParse(value)) {
    throw new Error("The visual project is missing its Russian web origin.");
  }
  return value;
}

function targetUrl(path: string, locale: ScreenshotLocale, testInfo: TestInfo) {
  return locale === "en" ? path : new URL(path, russianBaseURL(testInfo)).href;
}

async function hideVolatileScreenshotText(
  page: Page,
  selectors: readonly string[] | undefined,
) {
  if (!selectors || selectors.length === 0) return;
  for (const selector of selectors) {
    await expect(page.locator(selector)).not.toHaveCount(0);
  }
  await page.locator(selectors.join(", ")).evaluateAll((elements) => {
    for (const element of elements) {
      element.replaceChildren(document.createTextNode("reference parity"));
      element.setAttribute("data-reference-parity-volatile", "true");
    }
  });
  await page.addStyleTag({
    content:
      "[data-reference-parity-volatile='true'] { visibility: hidden !important; }",
  });
}

async function removeFrameworkDevTools(page: Page) {
  await page.locator("nextjs-portal").evaluateAll((elements) => {
    for (const element of elements) element.remove();
  });
}

async function pinCanonicalScreenshotFonts(page: Page) {
  await page.addStyleTag({ content: canonicalScreenshotFontStyle });
}

async function waitForDomQuiet(page: Page) {
  await page.evaluate(
    () =>
      new Promise<void>((resolve) => {
        let quietTimer = window.setTimeout(done, 300);
        const maximumTimer = window.setTimeout(done, 2_000);
        const observer = new MutationObserver(() => {
          window.clearTimeout(quietTimer);
          quietTimer = window.setTimeout(done, 300);
        });

        function done() {
          observer.disconnect();
          window.clearTimeout(quietTimer);
          window.clearTimeout(maximumTimer);
          resolve();
        }

        observer.observe(document.documentElement, {
          attributes: true,
          characterData: true,
          childList: true,
          subtree: true,
        });
      }),
  );
}

const screenshotOptions = {
  animations: "disabled",
  caret: "hide",
  fullPage: true,
  scale: "css",
  timeout: 15_000,
} as const;

async function captureInnerScrollState(
  page: Page,
  route: ReferenceParityRoute,
  locale: ScreenshotLocale,
  state: NonNullable<ReferenceParityRoute["scrollStates"]>[number],
  fixture: ReferenceParityFixture,
) {
  const scrollContainerSelector =
    route.authentication === "authenticated"
      ? "[data-slot='sidebar-inset']"
      : "main[data-documents-scroll-container]";
  const anchor = page.locator(state.anchorSelector).first();
  await expect(anchor).toHaveCount(1);
  const position = await anchor.evaluate(async (element, containerSelector) => {
    const container = document.querySelector(containerSelector);
    if (!(container instanceof HTMLElement)) {
      throw new Error(`Missing inner scroll container: ${containerSelector}`);
    }
    element.scrollIntoView({ block: "start", inline: "nearest" });
    await new Promise<void>((resolve) =>
      requestAnimationFrame(() => requestAnimationFrame(() => resolve())),
    );
    const anchorBounds = element.getBoundingClientRect();
    const containerBounds = container.getBoundingClientRect();
    return {
      anchorBottom: anchorBounds.bottom,
      anchorTop: anchorBounds.top,
      containerBottom: containerBounds.bottom,
      containerTop: containerBounds.top,
      scrollTop: container.scrollTop,
    };
  }, scrollContainerSelector);
  expect(position.scrollTop).toBeGreaterThan(0);
  expect(position.anchorBottom).toBeGreaterThan(position.containerTop);
  expect(position.anchorTop).toBeLessThan(position.containerBottom);
  await expect(anchor).toBeVisible();
  await fixture.assertSafeScreenshot(page);
  await expect(page).toHaveScreenshot(
    `${route.id}-${locale}-${state.id}.png`,
    screenshotOptions,
  );
}

async function expectProtectedMain(page: Page) {
  const main = page.getByRole("main");
  await expect(main).toHaveCount(1);
  await expect(main).toHaveAttribute("id", "main-content");
}

async function captureRoute(
  fixturePage: Page,
  route: ReferenceParityRoute,
  pathFixture: ReferenceParityPathFixture,
  fixture: ReferenceParityFixture,
  locale: ScreenshotLocale,
  testInfo: TestInfo,
) {
  const page = await fixturePage.context().newPage();
  try {
    const path = buildReferenceParityPath(route, pathFixture);
    await page.goto(targetUrl(path, locale, testInfo));

    if (route.authentication === "authenticated") {
      await waitForApplicationShell(page);
      await expectProtectedMain(page);
    }
    await expect(page.locator(route.readySelector).first()).toBeVisible();
    if (route.captureReadySelector) {
      await expect(
        page.locator(route.captureReadySelector).first(),
      ).toBeAttached();
    }
    const currentUrl = new URL(page.url());
    expect(`${currentUrl.pathname}${currentUrl.search}`).toBe(
      buildReferenceParityExpectedPath(route, pathFixture),
    );

    const dark = testInfo.project.name.endsWith("-dark");
    if (dark) {
      await expect(page.locator("html")).toHaveClass(/\bdark\b/u);
    } else {
      await expect(page.locator("html")).not.toHaveClass(/\bdark\b/u);
    }
    await page.evaluate(async () => {
      await document.fonts.ready;
    });
    await waitForDomQuiet(page);
    await pinCanonicalScreenshotFonts(page);
    await fixture.assertSafeScreenshot(page);
    await hideVolatileScreenshotText(page, route.volatileSelectors);
    await removeFrameworkDevTools(page);
    await expect(page).toHaveScreenshot(
      `${route.id}-${locale}.png`,
      screenshotOptions,
    );
    for (const state of route.scrollStates ?? []) {
      await captureInnerScrollState(page, route, locale, state, fixture);
    }
  } finally {
    await page.close();
  }
}

async function disableNextDevIndicator(page: Page, testInfo: TestInfo) {
  const responses = await Promise.all([
    page.context().request.post("/__nextjs_disable_dev_indicator"),
    page
      .context()
      .request.post(
        new URL("/__nextjs_disable_dev_indicator", russianBaseURL(testInfo))
          .href,
      ),
  ]);
  expect(responses.map((response) => response.status())).toEqual([204, 204]);
}

test("all migrated routes match the reference visual matrix", async ({
  organizationScenario,
  page,
}, testInfo) => {
  test.setTimeout(360_000);
  const fixture = await createReferenceParityFixture(
    page,
    organizationScenario,
    testInfo.project.name,
  );
  await disableNextDevIndicator(page, testInfo);
  const beforeOrganization: ReferenceParityPathFixture = {
    invitationId: fixture.invitationId,
    organizationKey: "",
  };
  let organizationFixture: ReferenceParityOrganizationFixture | undefined;

  for (const route of referenceParityRoutes.filter(
    ({ authentication }) => authentication === "anonymous",
  )) {
    await captureRoute(
      page,
      route,
      beforeOrganization,
      fixture,
      "en",
      testInfo,
    );
    if (referenceParityRussianOverflowRouteIds.includes(route.id as never)) {
      await captureRoute(
        page,
        route,
        beforeOrganization,
        fixture,
        "ru",
        testInfo,
      );
    }
  }

  await fixture.signIn(page);
  for (const route of referenceParityRoutes.filter(
    ({ authentication }) => authentication === "authenticated",
  )) {
    if (
      "requiresOrganization" in route &&
      route.requiresOrganization &&
      !organizationFixture
    ) {
      organizationFixture = await fixture.createOrganizationFixture();
    }
    const pathFixture = organizationFixture
      ? fixture.pathFixture()
      : beforeOrganization;
    if (route.id === "workspace-settings") {
      await fixture.ensureAlternateOrganization();
    }
    await captureRoute(page, route, pathFixture, fixture, "en", testInfo);
    if (referenceParityRussianOverflowRouteIds.includes(route.id as never)) {
      await captureRoute(page, route, pathFixture, fixture, "ru", testInfo);
    }
  }
});
