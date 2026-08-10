import {
  buildReferenceParityPath,
  referenceParityRoutes,
  referenceParityRussianOverflowRouteIds,
} from "@/e2e/support/ui-reference-parity";

const expectedRouteIds = [
  "home",
  "login",
  "auth-error",
  "docs",
  "docs-article",
  "welcome",
  "workspaces",
  "dashboard",
  "organization-dashboard",
  "user-profile",
  "user-connections",
  "user-security",
  "user-danger",
  "user-api-keys",
  "user-invitations",
  "workspace-settings",
  "workspace-members",
  "workspace-roles",
  "workspace-teams",
  "workspace-invitations",
  "workspace-api-keys",
  "invitation-decision",
] as const;

test("visual matrix includes every migrated route family", () => {
  expect(referenceParityRoutes.map(({ id }) => id)).toEqual(
    expect.arrayContaining(expectedRouteIds),
  );
});

test("visual matrix has one deterministic entry per migrated route", () => {
  expect(referenceParityRoutes.map(({ id }) => id)).toEqual(expectedRouteIds);
  expect(new Set(referenceParityRoutes.map(({ id }) => id))).toHaveProperty(
    "size",
    expectedRouteIds.length,
  );
});

test("dynamic paths use the E2E-created organization key and invitation ID", () => {
  const fixture = {
    invitationId: "invitation-created-by-e2e",
    organizationKey: "organization-created-by-e2e",
  };

  expect(
    buildReferenceParityPath(
      referenceParityRoutes.find(({ id }) => id === "organization-dashboard")!,
      fixture,
    ),
  ).toBe("/w/organization-created-by-e2e/dashboard");
  expect(
    buildReferenceParityPath(
      referenceParityRoutes.find(({ id }) => id === "workspace-invitations")!,
      fixture,
    ),
  ).toBe("/w/organization-created-by-e2e/settings/invitations");
  expect(
    buildReferenceParityPath(
      referenceParityRoutes.find(({ id }) => id === "invitation-decision")!,
      fixture,
    ),
  ).toBe("/invite/invitation-created-by-e2e");
});

test("Russian overflow pass is limited to the approved representative routes", () => {
  expect(referenceParityRussianOverflowRouteIds).toEqual([
    "docs",
    "user-profile",
    "workspace-settings",
    "workspace-invitations",
    "workspace-api-keys",
  ]);
});
