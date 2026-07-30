/** @jest-environment node */

import {
  finalizeOrganizationTeardown,
  OrganizationTeardownRegistry,
  preflightLocalAutomationUsers,
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

it("preflights fixed retry identities member-before-owner", async () => {
  const calls: string[] = [];
  const identities = [
    { email: "owner@local-agent.test", name: "owner", password: "secret" },
    { email: "member@local-agent.test", name: "member", password: "secret" },
  ] as const;

  await preflightLocalAutomationUsers(identities, async (identity) => {
    calls.push(identity.name);
    return { found: identity.name === "owner", deletedOrganizations: 1 };
  });

  expect(calls).toEqual(["member", "owner"]);
});
