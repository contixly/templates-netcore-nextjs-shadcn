type CleanupResult = Readonly<{
  deletedOrganizations: number | string;
}>;

type TeardownAction = Readonly<{
  label: string;
  run: () => Promise<void>;
}>;

export type TrackedLocalUser = {
  expectedDeletedOrganizations: number;
  readonly label: string;
  readonly organizationIds: Set<string>;
};

export class LocalAutomationScenarioCreationError extends Error {
  constructor(
    readonly status: number | undefined,
    readonly code: string | undefined,
  ) {
    super(
      `Local account creation failed with ${status ?? 0} (${code ?? "unknown"}).`,
    );
    this.name = "LocalAutomationScenarioCreationError";
  }
}

function isExistingLocalAutomationUser(
  error: unknown,
): error is LocalAutomationScenarioCreationError {
  return (
    error instanceof LocalAutomationScenarioCreationError &&
    error.status === 409 &&
    error.code === "local_auth_user_exists"
  );
}

export async function createLocalAutomationUsersWithConflictRecovery<
  TIdentity,
  TScenario,
>(
  identitiesInCreationOrder: readonly TIdentity[],
  operations: Readonly<{
    create: (identity: TIdentity) => Promise<TScenario>;
    onCreated: (identity: TIdentity, scenario: TScenario) => void;
    recoverExisting: (identity: TIdentity) => Promise<void>;
  }>,
): Promise<TScenario[]> {
  const conflicts: number[] = [];
  const scenarios = new Map<number, TScenario>();

  for (const [index, identity] of identitiesInCreationOrder.entries()) {
    try {
      const scenario = await operations.create(identity);
      operations.onCreated(identity, scenario);
      scenarios.set(index, scenario);
    } catch (error) {
      if (!isExistingLocalAutomationUser(error)) {
        throw error;
      }
      conflicts.push(index);
    }
  }

  for (const index of conflicts.toReversed()) {
    await operations.recoverExisting(identitiesInCreationOrder[index]);
  }

  for (const index of conflicts) {
    const identity = identitiesInCreationOrder[index];
    const scenario = await operations.create(identity);
    operations.onCreated(identity, scenario);
    scenarios.set(index, scenario);
  }

  return identitiesInCreationOrder.map((_, index) => {
    const scenario = scenarios.get(index);
    if (scenario === undefined) {
      throw new Error(`Missing local automation scenario at index ${index}.`);
    }
    return scenario;
  });
}

export class OrganizationTeardownRegistry {
  readonly #actions: TeardownAction[] = [];
  readonly #createdUsers = new Set<TrackedLocalUser>();

  trackContext(label: string, close: () => Promise<void>) {
    this.#actions.push({ label, run: close });
  }

  trackLocalUser(
    label: string,
    cleanup: () => Promise<CleanupResult>,
  ): TrackedLocalUser {
    const tracked = this.reserveLocalUser(label, cleanup);
    this.localUserCreated(tracked);
    return tracked;
  }

  reserveLocalUser(
    label: string,
    cleanup: () => Promise<CleanupResult>,
  ): TrackedLocalUser {
    const tracked: TrackedLocalUser = {
      expectedDeletedOrganizations: 0,
      label,
      organizationIds: new Set(),
    };
    this.#actions.push({
      label,
      run: async () => {
        if (!this.#createdUsers.has(tracked)) {
          return;
        }
        const result = await cleanup();
        const deletedOrganizations = Number(result.deletedOrganizations);
        if (deletedOrganizations !== tracked.expectedDeletedOrganizations) {
          throw new Error(
            `${label} cleanup deleted ${deletedOrganizations} organizations; expected ${tracked.expectedDeletedOrganizations}`,
          );
        }
      },
    });
    return tracked;
  }

  localUserCreated(user: TrackedLocalUser) {
    this.#createdUsers.add(user);
  }

  organizationCreated(user: TrackedLocalUser, organizationId?: string) {
    if (organizationId) {
      if (user.organizationIds.has(organizationId)) {
        throw new Error(
          `${user.label} organization ${organizationId} was already accounted as created`,
        );
      }
      user.organizationIds.add(organizationId);
    }
    user.expectedDeletedOrganizations += 1;
  }

  organizationDeleted(user: TrackedLocalUser, organizationId?: string) {
    if (organizationId) {
      if (!user.organizationIds.delete(organizationId)) {
        throw new Error(
          `${user.label} organization ${organizationId} was not accounted as created`,
        );
      }
    }
    user.expectedDeletedOrganizations -= 1;
  }

  async teardown(): Promise<Error[]> {
    const failures: Error[] = [];

    for (const action of this.#actions.toReversed()) {
      try {
        await action.run();
      } catch (error) {
        failures.push(
          error instanceof Error
            ? error
            : new Error(`${action.label} teardown failed: ${String(error)}`),
        );
      }
    }

    return failures;
  }
}

export async function finalizeOrganizationTeardown(
  registry: OrganizationTeardownRegistry,
  options: Readonly<{
    scenarioFailed: boolean;
    onSuppressedFailure?: (failure: Error) => void;
  }>,
) {
  const failures = await registry.teardown();
  if (failures.length === 0) {
    return;
  }

  if (options.scenarioFailed) {
    for (const failure of failures) {
      options.onSuppressedFailure?.(failure);
    }
    return;
  }

  if (failures.length === 1) {
    throw failures[0];
  }

  throw new AggregateError(failures, "Organization scenario teardown failed.");
}
