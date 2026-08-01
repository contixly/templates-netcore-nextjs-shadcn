import { fireEvent, screen, waitFor, within } from "@testing-library/react";

import { TeamDirectory } from "@/src/components/collaboration/team-directory";
import { getTeamMembers, getTeams } from "@/src/lib/api/generated/sdk.gen";
import type { TeamResponse } from "@/src/lib/api/generated/types.gen";
import { renderWithMessages, withMessages } from "@/test/support/render";

const refresh = jest.fn();
jest.mock("next/navigation", () => ({
  useRouter: () => ({ refresh }),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/generated/sdk.gen", () => ({
  getTeamMembers: jest.fn(),
  getTeamMemberCandidates: jest.fn(),
  getTeams: jest.fn(),
}));
jest.mock(
  "@/src/lib/api/collaboration/browser/collaboration-mutations",
  () => ({
    addBrowserTeamMember: jest.fn(),
    createBrowserTeam: jest.fn(),
    deleteBrowserTeam: jest.fn(),
    removeBrowserTeamMember: jest.fn(),
    updateBrowserTeam: jest.fn(),
  }),
);

const firstMember = {
  id: "membership-1",
  userId: "user-1",
  name: "Alice Admin",
  email: "alice@example.test",
  imageUrl: null,
  role: "admin" as const,
  organizationJoinedAt: "2026-08-01T00:00:00Z",
  teamJoinedAt: "2026-08-01T01:00:00Z",
};

const platform: TeamResponse = {
  id: "team-1",
  organizationId: "org-1",
  name: "Platform",
  memberCount: 51,
  members: { items: [firstMember], nextCursor: "member-next" },
  createdAt: "2026-08-01T00:00:00Z",
  updatedAt: "2026-08-01T00:00:00Z",
};

beforeEach(() => {
  jest.clearAllMocks();
});

it("renders every member's team view as read-only and never exposes active-team controls", () => {
  renderWithMessages(
    <TeamDirectory
      initialPage={{ items: [platform], nextCursor: null }}
      organization={{ id: "org-1", canManageTeams: false }}
    />,
  );

  const directory = screen.getByRole("region", { name: "Workspace teams" });
  expect(within(directory).getByText("Platform")).toBeVisible();
  expect(within(directory).getByText("Alice Admin")).toBeVisible();
  expect(
    screen.queryByRole("button", { name: /create team/i }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByText(/active team|set active|clear active/i),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", {
      name: /rename|delete|add member|remove member/i,
    }),
  ).not.toBeInTheDocument();
});

it("reconciles a refreshed server page without remounting immutable organization and team identities", () => {
  const view = renderWithMessages(
    <TeamDirectory
      initialPage={{ items: [platform], nextCursor: "old-next" }}
      organization={{ id: "org-1", canManageTeams: false }}
    />,
  );
  const refreshedMember = {
    ...firstMember,
    id: "membership-2",
    userId: "user-2",
    name: "Bob Member",
  };

  view.rerender(
    withMessages(
      <TeamDirectory
        initialPage={{
          items: [
            {
              ...platform,
              name: "Platform API",
              memberCount: 1,
              members: { items: [refreshedMember], nextCursor: null },
            },
          ],
          nextCursor: null,
        }}
        organization={{ id: "org-1", canManageTeams: false }}
      />,
    ),
  );

  expect(screen.getByText("Platform API")).toBeVisible();
  expect(screen.getByText("Bob Member")).toBeVisible();
  expect(screen.queryByText("Alice Admin")).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Load more teams" }),
  ).not.toBeInTheDocument();
});

it("appends team pages without replacing already visible results", async () => {
  jest.mocked(getTeams).mockResolvedValue({
    data: {
      data: {
        items: [{ ...platform, id: "team-2", name: "Design" }],
        nextCursor: null,
      },
    },
  } as Awaited<ReturnType<typeof getTeams>>);

  renderWithMessages(
    <TeamDirectory
      initialPage={{ items: [platform], nextCursor: "team-next" }}
      organization={{ id: "org-1", canManageTeams: false }}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "Load more teams" }));

  expect(await screen.findByText("Design")).toBeVisible();
  expect(screen.getByText("Platform")).toBeVisible();
  expect(getTeams).toHaveBeenCalledWith(
    expect.objectContaining({
      path: { organizationId: "org-1" },
      query: { cursor: "team-next", limit: 20 },
    }),
  );
});

it("continues beyond the embedded member page and keeps partial data on a retryable failure", async () => {
  jest
    .mocked(getTeamMembers)
    .mockResolvedValueOnce({
      error: { code: "api_unavailable" },
      response: { status: 503 } as Response,
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>)
    .mockResolvedValueOnce({
      data: {
        data: {
          items: [
            {
              ...firstMember,
              id: "membership-2",
              userId: "user-2",
              name: "Bob Member",
            },
          ],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getTeamMembers>>);

  renderWithMessages(
    <TeamDirectory
      initialPage={{ items: [platform], nextCursor: null }}
      organization={{ id: "org-1", canManageTeams: false }}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));

  expect(
    await screen.findByText("Some team members could not be loaded."),
  ).toBeVisible();
  expect(screen.getByText("Alice Admin")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "Retry" }));
  expect(await screen.findByText("Bob Member")).toBeVisible();
  await waitFor(() =>
    expect(
      screen.queryByText("Some team members could not be loaded."),
    ).not.toBeInTheDocument(),
  );
});
