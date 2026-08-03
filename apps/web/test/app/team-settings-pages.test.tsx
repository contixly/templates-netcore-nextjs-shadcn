import { render, screen } from "@testing-library/react";
import { isValidElement, type ReactElement, type ReactNode } from "react";

import TeamsSwitcherSlot from "@/src/app/(protected)/@applicationNavigation/w/[organizationKey]/settings/teams/page";
import TeamsPage from "@/src/app/(protected)/w/[organizationKey]/settings/teams/page";
import { TeamDirectory } from "@/src/components/collaboration/team-directory";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import type {
  OrganizationDetailResponse,
  TeamResponse,
} from "@/src/lib/api/generated/types.gen";
import { loadTeams } from "@/src/lib/api/collaboration/server/load-teams";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { withMessages } from "@/test/support/render";

jest.mock("next/server", () => ({ connection: jest.fn() }));
jest.mock("next/navigation", () => ({
  redirect: jest.fn((href: string) => {
    throw new Error(`NEXT_REDIRECT:${href}`);
  }),
  useRouter: () => ({ refresh: jest.fn() }),
}));
jest.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) =>
    ({
      title: "Workspace teams",
      description: "Organize workspace members into teams.",
      sectionTitle: "Team directory",
    })[key] ?? key,
}));
jest.mock("@/src/lib/api/auth/server/load-server-auth-session", () => ({
  loadServerAuthSession: jest.fn(),
}));
jest.mock("@/src/lib/api/organizations/server/load-organization", () => ({
  loadOrganization: jest.fn(),
}));
jest.mock("@/src/lib/api/collaboration/server/load-teams", () => ({
  loadTeams: jest.fn(),
}));
const organization: OrganizationDetailResponse = {
  id: "org-1",
  name: "Acme",
  slug: "acme",
  canonicalKey: "acme",
  createdAt: "2026-08-01T00:00:00Z",
  updatedAt: "2026-08-01T00:00:00Z",
  accessPrincipal: "user",
  currentRole: "owner",
  capabilities: {
    canUpdateOrganization: true,
    canDeleteOrganization: true,
    canAddMembers: true,
    canUpdateMemberRoles: true,
    canManageTeams: true,
    canManageInvitations: true,
    canManageApiKeys: true,
  },
  allowedEmailDomains: [],
};

const team: TeamResponse = {
  id: "team-1",
  organizationId: organization.id,
  name: "Platform",
  memberCount: 0,
  membersIncluded: true,
  members: { items: [], nextCursor: null },
  createdAt: "2026-08-01T00:00:00Z",
  updatedAt: "2026-08-01T00:00:00Z",
};

function findElementByType(
  node: ReactNode,
  type: ReactElement["type"],
): ReactElement | null {
  if (!isValidElement(node)) return null;
  if (node.type === type) return node;
  const children = (node.props as { children?: ReactNode }).children;
  for (const child of Array.isArray(children) ? children : [children]) {
    const found = findElementByType(child, type);
    if (found) return found;
  }
  return null;
}

beforeEach(() => {
  jest.clearAllMocks();
  jest.mocked(loadServerAuthSession).mockResolvedValue({
    ok: true,
    data: {
      authenticated: true,
      user: {
        id: "user-1",
        name: "Owner",
        email: "owner@example.test",
        emailVerified: true,
        image: null,
      },
      session: {
        id: "session-1",
        createdAt: "2026-08-01T00:00:00Z",
        updatedAt: "2026-08-01T00:00:00Z",
        expiresAt: "2026-08-02T00:00:00Z",
        activeOrganizationId: organization.id,
      },
    },
  });
  jest.mocked(loadOrganization).mockResolvedValue({
    ok: true,
    data: organization,
  });
  jest.mocked(loadTeams).mockResolvedValue({
    ok: true,
    data: { items: [team], nextCursor: "teams-next" },
  });
});

it("canonicalizes the workspace key before rendering team state", async () => {
  jest.mocked(loadOrganization).mockResolvedValue({
    ok: true,
    data: { ...organization, canonicalKey: "canonical-acme" },
  });

  await expect(
    TeamsPage({
      params: Promise.resolve({ organizationKey: organization.id }),
    }),
  ).rejects.toThrow("NEXT_REDIRECT:/w/canonical-acme/settings/teams");
  expect(loadTeams).not.toHaveBeenCalled();
});

it("loads the first REST page for SSR and keys the directory by immutable organization id", async () => {
  const page = await TeamsPage({
    params: Promise.resolve({ organizationKey: "acme" }),
  });
  const directory = findElementByType(page, TeamDirectory);

  expect(loadTeams).toHaveBeenCalledWith(organization.id, { limit: 20 });
  expect(directory).not.toBeNull();
  expect(directory!.key).toBe(organization.id);
  expect(directory!.props).toMatchObject({
    organization: { id: organization.id, canManageTeams: true },
    initialPage: { items: [team], nextCursor: "teams-next" },
  });

  const view = render(withMessages(page));
  expect(
    screen.getByRole("heading", { level: 1, name: "Workspace teams" }),
  ).toBeVisible();
  expect(screen.getByText("Platform").closest("article")).toHaveAttribute(
    "data-mode",
    "wide",
  );
  const headings = Array.from(
    view.container.querySelectorAll("h1, h2"),
    (heading) => heading.textContent?.trim(),
  );
  expect(new Set(headings).size).toBe(headings.length);
  expect(screen.getByRole("region", { name: "Team directory" })).toBeVisible();
});

it("uses the matching application-navigation return path", async () => {
  const slot = await TeamsSwitcherSlot({
    params: Promise.resolve({ organizationKey: "acme" }),
  });
  expect(slot.props).toEqual({
    redirectPath: "/w/acme/settings/teams",
    organizationKey: "acme",
  });
});
