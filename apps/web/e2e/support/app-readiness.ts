import { expect, type Locator, type Page } from "@playwright/test";

import { APP_HYDRATED_ATTRIBUTE } from "../../src/features/application/ui/app-hydration-marker";
import { INTERACTION_READY_ATTRIBUTE } from "../../src/features/application/ui/interaction-readiness";
import { ORGANIZATION_CONTROL_INTERACTION_READY_ATTRIBUTE } from "../../src/features/organizations/ui/organization-control-readiness";

const APPLICATION_NAVIGATION_TIMEOUT_MS = 15_000;

export async function waitForNavigationReady(
  page: Page,
  expectedUrl: string | RegExp,
  readyLocator: Locator,
) {
  await expect(page).toHaveURL(expectedUrl, {
    timeout: APPLICATION_NAVIGATION_TIMEOUT_MS,
  });
  await expect(readyLocator).toBeVisible({
    timeout: APPLICATION_NAVIGATION_TIMEOUT_MS,
  });
}

export async function waitForOrganizationControlInteraction(locator: Locator) {
  await expect(locator).toHaveAttribute(
    ORGANIZATION_CONTROL_INTERACTION_READY_ATTRIBUTE,
    "true",
  );
  await expect(locator).toBeEnabled();
}

export async function waitForInteraction(locator: Locator) {
  await expect(locator).toHaveAttribute(INTERACTION_READY_ATTRIBUTE, "true");
  await expect(locator).toBeEnabled();
}

export async function waitForApplicationShell(page: Page) {
  await expect(
    page.locator("[data-application-shell-ready='true']:visible"),
  ).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute(
    APP_HYDRATED_ATTRIBUTE,
    "true",
  );
}
