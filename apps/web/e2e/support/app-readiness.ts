import { expect, type Page } from "@playwright/test";

import { APP_HYDRATED_ATTRIBUTE } from "../../src/components/application/app-hydration-marker";

export async function waitForAppHydration(page: Page) {
  await expect(page.locator("html")).toHaveAttribute(
    APP_HYDRATED_ATTRIBUTE,
    "true",
  );
}
