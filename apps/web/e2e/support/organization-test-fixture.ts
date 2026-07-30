import {
  test as base,
  type APIRequestContext,
  type Browser,
  type BrowserContext,
} from "@playwright/test";

import type {
  LocalAutomationScenarioResponse,
  OrganizationDetailResponse,
} from "../../src/lib/api/generated";
import {
  cleanupExistingLocalAutomationUser,
  cleanupLocalAutomationUser,
  createLocalAutomationUser,
} from "./generated-auth-api";
import {
  createGeneratedOrganization,
  deleteGeneratedOrganization,
} from "./generated-organizations-api";
import {
  finalizeOrganizationTeardown,
  OrganizationTeardownRegistry,
  preflightLocalAutomationUsers,
  type TrackedLocalUser,
} from "./organization-e2e-harness";

// A fixture timeout gives teardown its own budget after the test timeout ends.
const ORGANIZATION_TEARDOWN_TIMEOUT_MS = 30_000;

export type OrganizationTestIdentity = Readonly<{
  email: string;
  name: string;
  password: string;
}>;

export type TrackedLocalAutomationScenario = LocalAutomationScenarioResponse & {
  readonly teardown: TrackedLocalUser;
};

class OrganizationScenario {
  readonly #browser: Browser;
  readonly #defaultRequest: APIRequestContext;
  readonly #registry: OrganizationTeardownRegistry;

  constructor(
    browser: Browser,
    defaultRequest: APIRequestContext,
    registry: OrganizationTeardownRegistry,
  ) {
    this.#browser = browser;
    this.#defaultRequest = defaultRequest;
    this.#registry = registry;
  }

  async preflightLocalUsers(
    identitiesInCreationOrder: readonly OrganizationTestIdentity[],
  ) {
    await preflightLocalAutomationUsers(
      identitiesInCreationOrder,
      async (identity) =>
        cleanupExistingLocalAutomationUser(
          this.#defaultRequest,
          identity.email,
          identity.password,
        ),
    );
  }

  async createContext(label: string): Promise<BrowserContext> {
    const context = await this.#browser.newContext();
    this.#registry.trackContext(label, () => context.close());
    return context;
  }

  async createLocalUser(
    context: BrowserContext,
    identity: OrganizationTestIdentity,
    label: string,
  ): Promise<TrackedLocalAutomationScenario> {
    const scenario = await createLocalAutomationUser(context.request, identity);
    const teardown = this.#registry.trackLocalUser(label, () =>
      cleanupLocalAutomationUser(context.request),
    );
    return { ...scenario, teardown };
  }

  organizationCreated(scenario: TrackedLocalAutomationScenario, count = 1) {
    for (let index = 0; index < count; index += 1) {
      this.#registry.organizationCreated(scenario.teardown);
    }
  }

  organizationDeleted(scenario: TrackedLocalAutomationScenario, count = 1) {
    for (let index = 0; index < count; index += 1) {
      this.#registry.organizationDeleted(scenario.teardown);
    }
  }

  async createOrganization(
    scenario: TrackedLocalAutomationScenario,
    request: APIRequestContext,
    name: string,
  ): Promise<OrganizationDetailResponse> {
    const organization = await createGeneratedOrganization(request, name);
    this.organizationCreated(scenario);
    return organization;
  }

  async deleteOrganization(
    scenario: TrackedLocalAutomationScenario,
    request: APIRequestContext,
    organization: OrganizationDetailResponse,
  ): Promise<string> {
    const organizationId = await deleteGeneratedOrganization(
      request,
      organization,
    );
    this.organizationDeleted(scenario);
    return organizationId;
  }
}

type OrganizationFixtures = {
  organizationScenario: OrganizationScenario;
};

export const test = base.extend<OrganizationFixtures>({
  organizationScenario: [
    async ({ browser, page }, use, testInfo) => {
      const registry = new OrganizationTeardownRegistry();
      const scenario = new OrganizationScenario(
        browser,
        page.context().request,
        registry,
      );

      await use(scenario);

      await finalizeOrganizationTeardown(registry, {
        scenarioFailed: testInfo.status !== testInfo.expectedStatus,
        onSuppressedFailure: (failure) => {
          testInfo.annotations.push({
            type: "organization-cleanup-error",
            description: failure.message,
          });
        },
      });
    },
    { box: true, timeout: ORGANIZATION_TEARDOWN_TIMEOUT_MS },
  ],
});

export { expect } from "@playwright/test";
