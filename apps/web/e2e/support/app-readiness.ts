import { expect, type Locator } from "@playwright/test";

import { INTERACTION_READY_ATTRIBUTE } from "../../src/components/application/interaction-readiness";
import { ORGANIZATION_CONTROL_INTERACTION_READY_ATTRIBUTE } from "../../src/components/organizations/organization-control-readiness";

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
