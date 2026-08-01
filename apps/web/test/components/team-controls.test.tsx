import { fireEvent, screen, waitFor, within } from "@testing-library/react";

import { TeamDirectory } from "@/src/components/collaboration/team-directory";
import {
  addBrowserTeamMember,
  createBrowserTeam,
  deleteBrowserTeam,
  removeBrowserTeamMember,
  updateBrowserTeam,
} from "@/src/lib/api/collaboration/browser/collaboration-mutations";
import {
  getTeamMemberCandidates,
  getTeamMembers,
  getTeams,
} from "@/src/lib/api/generated/sdk.gen";
import type { TeamResponse } from "@/src/lib/api/generated/types.gen";
import { renderWithMessages } from "@/test/support/render";

const refresh = jest.fn();
jest.mock("next/navigation", () => ({ useRouter: () => ({ refresh }) }));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/generated/sdk.gen", () => ({
  getTeamMemberCandidates: jest.fn(),
  getTeamMembers: jest.fn(),
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

const member = {
  id: "membership-1",
  userId: "user-1",
  name: "Alice Admin",
  email: "alice@example.test",
  imageUrl: null,
  role: "admin" as const,
  organizationJoinedAt: "2026-08-01T00:00:00Z",
  teamJoinedAt: "2026-08-01T01:00:00Z",
};
const team: TeamResponse = {
  id: "team-1",
  organizationId: "org-1",
  name: "Platform",
  memberCount: 1,
  members: { items: [member], nextCursor: null },
  createdAt: "2026-08-01T00:00:00Z",
  updatedAt: "2026-08-01T00:00:00Z",
};

function renderManager() {
  return renderWithMessages(
    <TeamDirectory
      initialPage={{ items: [team], nextCursor: null }}
      organization={{ id: "org-1", canManageTeams: true }}
    />,
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  jest.mocked(getTeams).mockResolvedValue({
    data: { data: { items: [team], nextCursor: null } },
  } as Awaited<ReturnType<typeof getTeams>>);
  jest.mocked(getTeamMembers).mockResolvedValue({
    data: { data: { items: [member], nextCursor: null } },
  } as Awaited<ReturnType<typeof getTeamMembers>>);
});

it("lets administrators create, rename, and confirm deletion with duplicate submission blocked", async () => {
  const created = {
    ...team,
    id: "team-2",
    name: "Design",
    members: { items: [], nextCursor: null },
    memberCount: 0,
  };
  jest.mocked(createBrowserTeam).mockResolvedValue({ ok: true, data: created });
  jest
    .mocked(updateBrowserTeam)
    .mockResolvedValue({ ok: true, data: { ...team, name: "Product" } });
  jest
    .mocked(deleteBrowserTeam)
    .mockResolvedValue({ ok: true, data: { teamId: team.id } });
  renderManager();

  fireEvent.click(screen.getByRole("button", { name: "Create team" }));
  const createDialog = screen.getByRole("dialog", { name: "Create team" });
  fireEvent.change(within(createDialog).getByLabelText("Team name"), {
    target: { value: "Design" },
  });
  fireEvent.submit(
    within(createDialog)
      .getByRole("button", { name: "Create team" })
      .closest("form")!,
  );
  fireEvent.submit(
    within(createDialog)
      .getByRole("button", { name: "Creating team" })
      .closest("form")!,
  );
  expect(await screen.findByText("Team created.")).toBeVisible();
  expect(createBrowserTeam).toHaveBeenCalledTimes(1);
  expect(screen.getByText("Design")).toBeVisible();

  fireEvent.click(screen.getByRole("button", { name: "Rename Platform" }));
  const renameDialog = screen.getByRole("dialog", { name: "Rename Platform" });
  fireEvent.change(within(renameDialog).getByLabelText("Team name"), {
    target: { value: "Product" },
  });
  fireEvent.click(
    within(renameDialog).getByRole("button", { name: "Rename team" }),
  );
  expect(await screen.findByText("Team renamed.")).toBeVisible();
  expect(screen.getByText("Product")).toBeVisible();

  fireEvent.click(screen.getByRole("button", { name: "Delete Product" }));
  const deleteDialog = screen.getByRole("dialog", { name: "Delete Product?" });
  fireEvent.click(
    within(deleteDialog).getByRole("button", { name: "Delete team" }),
  );
  expect(await screen.findByText("Team deleted.")).toBeVisible();
  expect(screen.queryByText("Product")).not.toBeInTheDocument();
});

it("searches and pages eligible workspace members before adding one", async () => {
  const bob = {
    memberId: "member-2",
    userId: "user-2",
    name: "Bob Member",
    email: "bob@example.test",
    imageUrl: null,
    role: "member" as const,
    joinedAt: "2026-08-01T00:00:00Z",
  };
  const carol = {
    ...bob,
    memberId: "member-3",
    userId: "user-3",
    name: "Carol Owner",
    role: "owner" as const,
  };
  jest
    .mocked(getTeamMemberCandidates)
    .mockResolvedValueOnce({
      data: { data: { items: [bob], nextCursor: "candidate-next" } },
    } as Awaited<ReturnType<typeof getTeamMemberCandidates>>)
    .mockResolvedValueOnce({
      data: { data: { items: [carol], nextCursor: null } },
    } as Awaited<ReturnType<typeof getTeamMemberCandidates>>);
  jest.mocked(addBrowserTeamMember).mockResolvedValue({
    ok: true,
    data: {
      ...member,
      id: "membership-2",
      userId: bob.userId,
      name: bob.name,
      email: bob.email,
      role: bob.role,
    },
  });
  renderManager();

  fireEvent.click(
    screen.getByRole("button", { name: "Add member to Platform" }),
  );
  const dialog = screen.getByRole("dialog", { name: "Add member to Platform" });
  fireEvent.change(within(dialog).getByLabelText("Find a workspace member"), {
    target: { value: "bob" },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Search" }));
  expect(await within(dialog).findByText("Bob Member")).toBeVisible();
  expect(getTeamMemberCandidates).toHaveBeenLastCalledWith(
    expect.objectContaining({ query: { q: "bob", limit: 20 } }),
  );
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Load more candidates" }),
  );
  expect(await within(dialog).findByText("Carol Owner")).toBeVisible();
  expect(getTeamMemberCandidates).toHaveBeenLastCalledWith(
    expect.objectContaining({
      query: { q: "bob", cursor: "candidate-next", limit: 20 },
    }),
  );
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Add Bob Member" }),
  );
  expect(await screen.findByText("Member added to the team.")).toBeVisible();
  expect(addBrowserTeamMember).toHaveBeenCalledWith(
    expect.anything(),
    "org-1",
    "team-1",
    { userId: "user-2" },
  );
});

it("keeps a confirmed removal visible and offers refresh recovery without leaking detail", async () => {
  jest.mocked(removeBrowserTeamMember).mockResolvedValue({
    ok: true,
    data: { teamId: team.id, userId: member.userId },
  });
  jest
    .mocked(getTeamMembers)
    .mockResolvedValueOnce({
      error: { code: "api_unavailable", detail: "private database topology" },
      response: { status: 503 } as Response,
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>)
    .mockResolvedValueOnce({
      data: { data: { items: [], nextCursor: null } },
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
  renderManager();

  fireEvent.click(screen.getByRole("button", { name: "Remove Alice Admin" }));
  expect(
    await screen.findByText(
      "The change was saved, but the latest team data could not be loaded.",
    ),
  ).toBeVisible();
  expect(screen.getByText("Member removed from the team.")).toBeVisible();
  expect(screen.queryByText("Alice Admin")).not.toBeInTheDocument();
  expect(
    screen.queryByText(/private database topology/i),
  ).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Retry" }));
  await waitFor(() =>
    expect(
      screen.queryByText(
        "The change was saved, but the latest team data could not be loaded.",
      ),
    ).not.toBeInTheDocument(),
  );
});
