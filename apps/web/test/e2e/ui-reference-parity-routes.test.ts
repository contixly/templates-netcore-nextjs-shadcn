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

test("inner-scroll evidence uses exact named route-specific anchors", () => {
  expect(
    Object.fromEntries(
      referenceParityRoutes
        .filter((route) => "scrollStates" in route)
        .map((route) => [route.id, route.scrollStates]),
    ),
  ).toEqual({
    "docs-article": [
      { anchorSelector: "#problem-details", id: "problem-details" },
    ],
    "organization-dashboard": [
      {
        anchorSelector: "main#main-content [data-slot='table-container']",
        id: "activity-table",
      },
    ],
    "user-profile": [
      { anchorSelector: "#account-display-name", id: "profile-form" },
    ],
    "workspace-api-keys": [
      {
        anchorSelector:
          "main#main-content [data-slot='settings-section'] table",
        id: "api-key-table",
      },
    ],
    "workspace-invitations": [
      {
        anchorSelector:
          "main#main-content [data-slot='settings-section'] table",
        id: "invitation-table",
      },
    ],
    "workspace-members": [
      {
        anchorSelector:
          "main#main-content [data-slot='settings-section'] table",
        id: "member-table",
      },
    ],
    "workspace-settings": [
      {
        anchorSelector: "#organization-settings-domains",
        id: "workspace-form",
      },
    ],
  });
});

test("top and inner-scroll state matrix has exactly 152 named screenshots", () => {
  const topScreenshots = referenceParityRoutes.length * 4;
  const russianTopScreenshots =
    referenceParityRussianOverflowRouteIds.length * 4;
  const englishScrollScreenshots = referenceParityRoutes.reduce(
    (total, route) =>
      total + ("scrollStates" in route ? route.scrollStates.length * 4 : 0),
    0,
  );
  const russianScrollScreenshots = referenceParityRoutes.reduce(
    (total, route) =>
      total +
      (referenceParityRussianOverflowRouteIds.includes(route.id as never) &&
      "scrollStates" in route
        ? route.scrollStates.length * 4
        : 0),
    0,
  );

  expect({
    englishScrollScreenshots,
    russianScrollScreenshots,
    russianTopScreenshots,
    topScreenshots,
    total:
      topScreenshots +
      russianTopScreenshots +
      englishScrollScreenshots +
      russianScrollScreenshots,
  }).toEqual({
    englishScrollScreenshots: 28,
    russianScrollScreenshots: 16,
    russianTopScreenshots: 20,
    topScreenshots: 88,
    total: 152,
  });
});
