import { expect, type Locator, type Page } from "@playwright/test";

import { APP_HYDRATED_ATTRIBUTE } from "../../src/components/application/app-hydration-marker";
import { ORGANIZATION_CONTROL_INTERACTION_READY_ATTRIBUTE } from "../../src/components/organizations/organization-control-readiness";

export async function waitForAppHydration(page: Page) {
  await expect(page.locator("html")).toHaveAttribute(
    APP_HYDRATED_ATTRIBUTE,
    "true",
  );
}

export async function waitForOrganizationControlInteraction(locator: Locator) {
  await expect(locator).toHaveAttribute(
    ORGANIZATION_CONTROL_INTERACTION_READY_ATTRIBUTE,
    "true",
  );
  await expect(locator).toBeEnabled();
}
