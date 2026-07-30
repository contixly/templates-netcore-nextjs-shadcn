/** @jest-environment node */

import {
  createLocalAutomationUsersWithConflictRecovery,
  finalizeOrganizationTeardown,
  LocalAutomationScenarioCreationError,
  OrganizationTeardownRegistry,
} from "@/e2e/support/organization-e2e-harness";

it("does not mask a forced scenario failure while attempting every reverse-order teardown step", async () => {
  const calls: string[] = [];
  const registry = new OrganizationTeardownRegistry();
  const owner = registry.trackLocalUser("owner", async () => {
    calls.push("cleanup owner");
    return { deletedOrganizations: 1 };
  });
  registry.organizationCreated(owner);
  registry.trackContext("member context", async () => {
    calls.push("close member context");
    throw new Error("forced context close failure");
  });
  registry.trackLocalUser("member", async () => {
    calls.push("cleanup member");
    throw new Error("forced member cleanup failure");
  });

  const suppressed: Error[] = [];
  await finalizeOrganizationTeardown(registry, {
    scenarioFailed: true,
    onSuppressedFailure: (failure) => suppressed.push(failure),
  });

  expect(calls).toEqual([
    "cleanup member",
    "close member context",
    "cleanup owner",
  ]);
  expect(suppressed.map((failure) => failure.message)).toEqual([
    "forced member cleanup failure",
    "forced context close failure",
  ]);
});

it("fails a successful scenario when an exact cleanup count does not match", async () => {
  const registry = new OrganizationTeardownRegistry();
  const owner = registry.trackLocalUser("owner", async () => ({
    deletedOrganizations: 0,
  }));
  registry.organizationCreated(owner);

  await expect(
    finalizeOrganizationTeardown(registry, { scenarioFailed: false }),
  ).rejects.toThrow("owner cleanup deleted 0 organizations; expected 1");
});

it("creates absent fixed identities without a preflight sign-in", async () => {
  const identities = ["owner", "member"] as const;
  const calls: string[] = [];
  const recoverExisting = jest.fn();

  const scenarios = await createLocalAutomationUsersWithConflictRecovery(
    identities,
    {
      create: async (identity) => {
        calls.push(`create ${identity}`);
        return { identity };
      },
      onCreated: (identity) => calls.push(`track ${identity}`),
      recoverExisting,
    },
  );

  expect(calls).toEqual([
    "create owner",
    "track owner",
    "create member",
    "track member",
  ]);
  expect(scenarios).toEqual([{ identity: "owner" }, { identity: "member" }]);
  expect(recoverExisting).not.toHaveBeenCalled();
});

it("recovers and retries only an exact local-user-exists conflict", async () => {
  const calls: string[] = [];
  let createAttempts = 0;

  const scenarios = await createLocalAutomationUsersWithConflictRecovery(
    ["owner"],
    {
      create: async (identity) => {
        calls.push(`create ${identity}`);
        createAttempts += 1;
        if (createAttempts === 1) {
          throw new LocalAutomationScenarioCreationError(
            409,
            "local_auth_user_exists",
          );
        }
        return { identity };
      },
      onCreated: (identity) => calls.push(`track ${identity}`),
      recoverExisting: async (identity) => {
        calls.push(`recover ${identity}`);
      },
    },
  );

  expect(calls).toEqual([
    "create owner",
    "recover owner",
    "create owner",
    "track owner",
  ]);
  expect(scenarios).toEqual([{ identity: "owner" }]);
});

it.each([
  [409, "organization_name_conflict"],
  [409, undefined],
  [400, "local_auth_user_exists"],
  [429, "rate_limited"],
  [500, "internal_error"],
  [undefined, "local_auth_user_exists"],
  [undefined, undefined],
] as const)(
  "fails closed for creation failure status %s and code %s",
  async (status, code) => {
    const failure = new LocalAutomationScenarioCreationError(status, code);
    const recoverExisting = jest.fn();

    await expect(
      createLocalAutomationUsersWithConflictRecovery(["owner"], {
        create: async () => {
          throw failure;
        },
        onCreated: jest.fn(),
        recoverExisting,
      }),
    ).rejects.toBe(failure);
    expect(recoverExisting).not.toHaveBeenCalled();
  },
);

it("recovers dependent stale identities member-before-owner before recreating them", async () => {
  const calls: string[] = [];
  const recovered = new Set<string>();
  const attempts = new Map<string, number>();

  await createLocalAutomationUsersWithConflictRecovery(["owner", "member"], {
    create: async (identity) => {
      calls.push(`create ${identity}`);
      const attempt = (attempts.get(identity) ?? 0) + 1;
      attempts.set(identity, attempt);
      if (attempt === 1) {
        throw new LocalAutomationScenarioCreationError(
          409,
          "local_auth_user_exists",
        );
      }
      return { identity };
    },
    onCreated: (identity) => calls.push(`track ${identity}`),
    recoverExisting: async (identity) => {
      calls.push(`recover ${identity}`);
      if (identity === "owner" && !recovered.has("member")) {
        throw new Error("owner cleanup ownership-blocked");
      }
      recovered.add(identity);
    },
  });

  expect(calls).toEqual([
    "create owner",
    "create member",
    "recover member",
    "recover owner",
    "create owner",
    "track owner",
    "create member",
    "track member",
  ]);
});

it("reserved final teardown remains member-context-owner even when recreation finishes owner-first", async () => {
  const calls: string[] = [];
  const registry = new OrganizationTeardownRegistry();
  const owner = registry.reserveLocalUser("owner", async () => {
    calls.push("cleanup owner");
    return { deletedOrganizations: 1 };
  });
  registry.trackContext("member context", async () => {
    calls.push("close member context");
  });
  const member = registry.reserveLocalUser("member", async () => {
    calls.push("cleanup member");
    return { deletedOrganizations: 0 };
  });
  registry.localUserCreated(owner);
  registry.localUserCreated(member);
  registry.organizationCreated(owner);

  await finalizeOrganizationTeardown(registry, { scenarioFailed: false });

  expect(calls).toEqual([
    "cleanup member",
    "close member context",
    "cleanup owner",
  ]);
});

it("configured retries do not spend the shared sign-in limit on absent identities", async () => {
  const identities = ["onboarding", "owner", "member", "slug"] as const;
  const recoverExisting = jest.fn();
  const create = jest.fn(async (identity: (typeof identities)[number]) => ({
    identity,
  }));

  for (let attempt = 0; attempt < 3; attempt += 1) {
    await createLocalAutomationUsersWithConflictRecovery(identities, {
      create,
      onCreated: jest.fn(),
      recoverExisting,
    });
  }

  expect(create).toHaveBeenCalledTimes(12);
  expect(recoverExisting).not.toHaveBeenCalled();
});
