type LocalAutomationIdentity = Readonly<{
  email: string;
  name: string;
  password: string;
}>;

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
};

export class OrganizationTeardownRegistry {
  readonly #actions: TeardownAction[] = [];

  trackContext(label: string, close: () => Promise<void>) {
    this.#actions.push({ label, run: close });
  }

  trackLocalUser(
    label: string,
    cleanup: () => Promise<CleanupResult>,
  ): TrackedLocalUser {
    const tracked: TrackedLocalUser = {
      expectedDeletedOrganizations: 0,
      label,
    };
    this.#actions.push({
      label,
      run: async () => {
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

  organizationCreated(user: TrackedLocalUser) {
    user.expectedDeletedOrganizations += 1;
  }

  organizationDeleted(user: TrackedLocalUser) {
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

export async function preflightLocalAutomationUsers<
  TIdentity extends LocalAutomationIdentity,
>(
  identitiesInCreationOrder: readonly TIdentity[],
  cleanup: (identity: TIdentity) => Promise<unknown>,
) {
  const failures: Error[] = [];

  for (const identity of identitiesInCreationOrder.toReversed()) {
    try {
      await cleanup(identity);
    } catch (error) {
      failures.push(
        error instanceof Error
          ? error
          : new Error(
              `Preflight cleanup failed for ${identity.email}: ${String(error)}`,
            ),
      );
    }
  }

  if (failures.length > 0) {
    throw new AggregateError(
      failures,
      "Organization scenario preflight cleanup failed.",
    );
  }
}
