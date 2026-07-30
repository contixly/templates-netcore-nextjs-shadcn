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
  cleanupLocalAutomationUser,
  createLocalAutomationUser,
  recoverExistingLocalAutomationUser,
} from "./generated-auth-api";
import {
  createGeneratedOrganization,
  deleteGeneratedOrganization,
} from "./generated-organizations-api";
import {
  createLocalAutomationUsersWithConflictRecovery,
  finalizeOrganizationTeardown,
  OrganizationTeardownRegistry,
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

type PreparedLocalAutomationUser = Readonly<{
  context: BrowserContext;
  identity: OrganizationTestIdentity;
  teardown: TrackedLocalUser;
}>;

class OrganizationScenario {
  readonly #browser: Browser;
  readonly #registry: OrganizationTeardownRegistry;

  constructor(browser: Browser, registry: OrganizationTeardownRegistry) {
    this.#browser = browser;
    this.#registry = registry;
  }

  async createContext(label: string): Promise<BrowserContext> {
    const context = await this.#browser.newContext();
    this.#registry.trackContext(label, () => context.close());
    return context;
  }

  prepareLocalUser(
    context: BrowserContext,
    identity: OrganizationTestIdentity,
    label: string,
  ): PreparedLocalAutomationUser {
    const teardown = this.#registry.reserveLocalUser(label, () =>
      cleanupLocalAutomationUser(context.request),
    );
    return { context, identity, teardown };
  }

  async createLocalUsers(
    preparedUsers: readonly PreparedLocalAutomationUser[],
  ): Promise<TrackedLocalAutomationScenario[]> {
    const scenarios = await createLocalAutomationUsersWithConflictRecovery(
      preparedUsers,
      {
        create: (prepared) =>
          createLocalAutomationUser(
            prepared.context.request,
            prepared.identity,
          ),
        onCreated: (prepared) => {
          this.#registry.localUserCreated(prepared.teardown);
        },
        recoverExisting: (prepared) =>
          recoverExistingLocalAutomationUser(
            prepared.context.request,
            prepared.identity.email,
            prepared.identity.password,
          ),
      },
    );

    return scenarios.map((scenario, index) => ({
      ...scenario,
      teardown: preparedUsers[index].teardown,
    }));
  }

  async createLocalUser(
    context: BrowserContext,
    identity: OrganizationTestIdentity,
    label: string,
  ): Promise<TrackedLocalAutomationScenario> {
    const prepared = this.prepareLocalUser(context, identity, label);
    const [scenario] = await this.createLocalUsers([prepared]);
    return scenario;
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
      // Keep the default page/context alive until organization cleanup finishes.
      void page;
      const registry = new OrganizationTeardownRegistry();
      const scenario = new OrganizationScenario(browser, registry);

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
