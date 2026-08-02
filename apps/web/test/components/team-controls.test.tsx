import {
  act,
  fireEvent,
  screen,
  waitFor,
  within,
} from "@testing-library/react";

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
import type {
  TeamMemberResponse,
  TeamResponse,
} from "@/src/lib/api/generated/types.gen";
import { renderWithMessages, withMessages } from "@/test/support/render";

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
  membersIncluded: true,
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

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((next) => {
    resolve = next;
  });
  return { promise, resolve };
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
    membersIncluded: true,
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

it("accepts exactly 50 supplementary-plane letters when creating and renaming teams", async () => {
  const fiftyUnicodeScalars = "\u{10400}".repeat(50);
  const created = {
    ...team,
    id: "team-2",
    name: fiftyUnicodeScalars,
    membersIncluded: true,
    members: { items: [], nextCursor: null },
    memberCount: 0,
  };
  jest.mocked(createBrowserTeam).mockResolvedValue({ ok: true, data: created });
  jest.mocked(updateBrowserTeam).mockResolvedValue({
    ok: true,
    data: { ...team, name: fiftyUnicodeScalars },
  });
  renderManager();

  fireEvent.click(screen.getByRole("button", { name: "Create team" }));
  let dialog = screen.getByRole("dialog", { name: "Create team" });
  fireEvent.change(within(dialog).getByLabelText("Team name"), {
    target: { value: fiftyUnicodeScalars },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Create team" }));

  await waitFor(() =>
    expect(createBrowserTeam).toHaveBeenCalledWith(expect.anything(), "org-1", {
      name: fiftyUnicodeScalars,
    }),
  );

  fireEvent.click(screen.getByRole("button", { name: "Rename Platform" }));
  dialog = screen.getByRole("dialog", { name: "Rename Platform" });
  fireEvent.change(within(dialog).getByLabelText("Team name"), {
    target: { value: fiftyUnicodeScalars },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Rename team" }));

  await waitFor(() =>
    expect(updateBrowserTeam).toHaveBeenCalledWith(
      expect.anything(),
      "org-1",
      "team-1",
      { name: fiftyUnicodeScalars },
    ),
  );
});

it.each([
  ["create", "\u{10400}".repeat(51)],
  ["create", "Valid\u{1f600}"],
  ["create", "Valid\ud800"],
  ["rename", "\u{10400}".repeat(51)],
  ["rename", "Valid\u{1f600}"],
  ["rename", "Valid\ud800"],
] as const)(
  "rejects an invalid Unicode team name during %s",
  async (action, name) => {
    renderManager();

    fireEvent.click(
      screen.getByRole("button", {
        name: action === "create" ? "Create team" : "Rename Platform",
      }),
    );
    const dialog = screen.getByRole("dialog", {
      name: action === "create" ? "Create team" : "Rename Platform",
    });
    fireEvent.change(within(dialog).getByLabelText("Team name"), {
      target: { value: name },
    });
    fireEvent.click(
      within(dialog).getByRole("button", {
        name: action === "create" ? "Create team" : "Rename team",
      }),
    );

    expect(within(dialog).getByRole("alert")).toHaveTextContent(
      "Use 1–50 letters, numbers, spaces, hyphens, or underscores.",
    );
    expect(createBrowserTeam).not.toHaveBeenCalled();
    expect(updateBrowserTeam).not.toHaveBeenCalled();
  },
);

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
  const bobUpdated = { ...bob, name: "Bob Updated" };
  const carolUpdated = { ...carol, name: "Carol Updated" };
  jest
    .mocked(getTeamMemberCandidates)
    .mockResolvedValueOnce({
      data: { data: { items: [bob], nextCursor: "candidate-next" } },
    } as Awaited<ReturnType<typeof getTeamMemberCandidates>>)
    .mockResolvedValueOnce({
      data: {
        data: {
          items: [bobUpdated, carol, carolUpdated],
          nextCursor: null,
        },
      },
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
  expect(await within(dialog).findByText("Bob Updated")).toBeVisible();
  expect(await within(dialog).findByText("Carol Updated")).toBeVisible();
  expect(within(dialog).getAllByText("Bob Updated")).toHaveLength(1);
  expect(within(dialog).getAllByText("Carol Updated")).toHaveLength(1);
  expect(getTeamMemberCandidates).toHaveBeenLastCalledWith(
    expect.objectContaining({
      query: { q: "bob", cursor: "candidate-next", limit: 20 },
    }),
  );
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Add Bob Updated" }),
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

it("discards an older member recovery and runs one latest recovery after a later mutation", async () => {
  const bobCandidate = {
    memberId: "member-2",
    userId: "user-2",
    name: "Bob Member",
    email: "bob@example.test",
    imageUrl: null,
    role: "member" as const,
    joinedAt: "2026-08-01T00:00:00Z",
  };
  const bobMember = {
    ...member,
    id: "membership-2",
    userId: bobCandidate.userId,
    name: bobCandidate.name,
    email: bobCandidate.email,
    role: bobCandidate.role,
  };
  const olderRecovery = deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const latestRecovery = deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  jest.mocked(getTeamMemberCandidates).mockResolvedValue({
    data: { data: { items: [bobCandidate], nextCursor: null } },
  } as Awaited<ReturnType<typeof getTeamMemberCandidates>>);
  jest.mocked(addBrowserTeamMember).mockResolvedValue({
    ok: true,
    data: bobMember,
  });
  jest.mocked(removeBrowserTeamMember).mockResolvedValue({
    ok: true,
    data: { teamId: team.id, userId: member.userId },
  });
  jest
    .mocked(getTeamMembers)
    .mockReturnValueOnce(
      olderRecovery.promise as ReturnType<typeof getTeamMembers>,
    )
    .mockReturnValueOnce(
      latestRecovery.promise as ReturnType<typeof getTeamMembers>,
    );
  renderManager();

  fireEvent.click(
    screen.getByRole("button", { name: "Add member to Platform" }),
  );
  const dialog = screen.getByRole("dialog", { name: "Add member to Platform" });
  fireEvent.change(within(dialog).getByLabelText("Find a workspace member"), {
    target: { value: "bob" },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Search" }));
  fireEvent.click(
    await within(dialog).findByRole("button", { name: "Add Bob Member" }),
  );
  await waitFor(() =>
    expect(
      screen.queryByRole("dialog", { name: "Add member to Platform" }),
    ).not.toBeInTheDocument(),
  );
  const memberDirectory = screen.getByRole("region", {
    name: "Platform members",
  });
  expect(await within(memberDirectory).findByText("Bob Member")).toBeVisible();
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(1));

  fireEvent.click(screen.getByRole("button", { name: "Remove Alice Admin" }));
  await waitFor(() =>
    expect(screen.queryByText("Alice Admin")).not.toBeInTheDocument(),
  );
  expect(getTeamMembers).toHaveBeenCalledTimes(1);

  olderRecovery.resolve({
    data: { data: { items: [member, bobMember], nextCursor: null } },
  } as Awaited<ReturnType<typeof getTeamMembers>>);
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(2));
  expect(screen.queryByText("Alice Admin")).not.toBeInTheDocument();

  latestRecovery.resolve({
    data: {
      data: {
        items: [{ ...bobMember, name: "Bob Reconciled" }],
        nextCursor: null,
      },
    },
  } as Awaited<ReturnType<typeof getTeamMembers>>);
  expect(await screen.findByText("Bob Reconciled")).toBeVisible();
  expect(screen.queryByText("Alice Admin")).not.toBeInTheDocument();
  expect(getTeamMembers).toHaveBeenCalledTimes(2);
});

it("does not let an older post-read overlay override a newer confirmed member mutation", async () => {
  const aliceCandidate = {
    memberId: "organization-membership-1",
    userId: member.userId,
    name: member.name,
    email: member.email,
    imageUrl: member.imageUrl,
    role: member.role,
    joinedAt: member.organizationJoinedAt,
  };
  const olderRecovery = deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const latestRecovery = deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const laterAdd = deferred<Awaited<ReturnType<typeof addBrowserTeamMember>>>();
  jest.mocked(removeBrowserTeamMember).mockResolvedValue({
    ok: true,
    data: { teamId: team.id, userId: member.userId },
  });
  jest.mocked(getTeamMemberCandidates).mockResolvedValue({
    data: { data: { items: [aliceCandidate], nextCursor: null } },
  } as Awaited<ReturnType<typeof getTeamMemberCandidates>>);
  jest.mocked(addBrowserTeamMember).mockReturnValue(laterAdd.promise);
  jest
    .mocked(getTeamMembers)
    .mockReturnValueOnce(
      olderRecovery.promise as ReturnType<typeof getTeamMembers>,
    )
    .mockReturnValueOnce(
      latestRecovery.promise as ReturnType<typeof getTeamMembers>,
    );
  renderManager();

  fireEvent.click(screen.getByRole("button", { name: "Remove Alice Admin" }));
  await waitFor(() =>
    expect(screen.queryByText("Alice Admin")).not.toBeInTheDocument(),
  );
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(1));

  fireEvent.click(
    screen.getByRole("button", { name: "Add member to Platform" }),
  );
  const dialog = screen.getByRole("dialog", { name: "Add member to Platform" });
  fireEvent.change(within(dialog).getByLabelText("Find a workspace member"), {
    target: { value: "alice" },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Search" }));
  fireEvent.click(
    await within(dialog).findByRole("button", { name: "Add Alice Admin" }),
  );
  await waitFor(() => expect(addBrowserTeamMember).toHaveBeenCalledTimes(1));

  const interleavedPage = {
    get items(): TeamMemberResponse[] {
      laterAdd.resolve({ ok: true, data: member });
      return [];
    },
    nextCursor: null,
  };
  olderRecovery.resolve({
    data: { data: interleavedPage },
  } as Awaited<ReturnType<typeof getTeamMembers>>);

  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(2));
  await waitFor(() => expect(screen.getByText("Alice Admin")).toBeVisible());

  latestRecovery.resolve({
    data: { data: { items: [member], nextCursor: null } },
  } as Awaited<ReturnType<typeof getTeamMembers>>);
  await waitFor(() => expect(screen.getByText("Alice Admin")).toBeVisible());
});

it("invalidates a stale team continuation success and its finally state when the RSC page changes", async () => {
  const staleContinuation = deferred<Awaited<ReturnType<typeof getTeams>>>();
  const currentContinuation = deferred<Awaited<ReturnType<typeof getTeams>>>();
  const refreshedTeam = { ...team, name: "Platform refreshed" };
  const staleTeam = {
    ...team,
    id: "team-stale",
    name: "Stale continuation team",
  };
  const currentTeam = {
    ...team,
    id: "team-current",
    name: "Current continuation team",
  };
  jest
    .mocked(getTeams)
    .mockReturnValueOnce(
      staleContinuation.promise as ReturnType<typeof getTeams>,
    )
    .mockReturnValueOnce(
      currentContinuation.promise as ReturnType<typeof getTeams>,
    );
  const view = renderWithMessages(
    <TeamDirectory
      initialPage={{ items: [team], nextCursor: "stale-cursor" }}
      organization={{ id: "org-1", canManageTeams: true }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Load more teams" }));
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(1));

  view.rerender(
    withMessages(
      <TeamDirectory
        initialPage={{ items: [refreshedTeam], nextCursor: "current-cursor" }}
        organization={{ id: "org-1", canManageTeams: true }}
      />,
    ),
  );
  expect(screen.getByText("Platform refreshed")).toBeVisible();
  const currentLoadMore = screen.getByRole("button", {
    name: "Load more teams",
  });
  expect(currentLoadMore).toBeEnabled();
  fireEvent.click(currentLoadMore);
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(2));

  await act(async () => {
    staleContinuation.resolve({
      data: {
        data: { items: [staleTeam], nextCursor: "stale-result-cursor" },
      },
    } as unknown as Awaited<ReturnType<typeof getTeams>>);
    await staleContinuation.promise;
  });

  expect(screen.queryByText("Stale continuation team")).not.toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "Loading more teams" }),
  ).toBeDisabled();

  await act(async () => {
    currentContinuation.resolve({
      data: { data: { items: [currentTeam], nextCursor: null } },
    } as unknown as Awaited<ReturnType<typeof getTeams>>);
    await currentContinuation.promise;
  });

  expect(screen.getByText("Current continuation team")).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "Load more teams" }),
  ).not.toBeInTheDocument();
});

it("invalidates a stale team continuation error without clearing a newer pending read", async () => {
  const staleContinuation = deferred<Awaited<ReturnType<typeof getTeams>>>();
  const currentContinuation = deferred<Awaited<ReturnType<typeof getTeams>>>();
  jest
    .mocked(getTeams)
    .mockReturnValueOnce(
      staleContinuation.promise as ReturnType<typeof getTeams>,
    )
    .mockReturnValueOnce(
      currentContinuation.promise as ReturnType<typeof getTeams>,
    );
  const view = renderWithMessages(
    <TeamDirectory
      initialPage={{ items: [team], nextCursor: "stale-cursor" }}
      organization={{ id: "org-1", canManageTeams: true }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Load more teams" }));
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(1));
  view.rerender(
    withMessages(
      <TeamDirectory
        initialPage={{ items: [team], nextCursor: "current-cursor" }}
        organization={{ id: "org-1", canManageTeams: true }}
      />,
    ),
  );
  fireEvent.click(screen.getByRole("button", { name: "Load more teams" }));
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(2));

  await act(async () => {
    staleContinuation.resolve({
      error: { code: "api_unavailable" },
      response: { status: 503 } as Response,
    } as unknown as Awaited<ReturnType<typeof getTeams>>);
    await staleContinuation.promise;
  });

  expect(
    screen.queryByText(
      "Some teams could not be loaded. The teams already shown are still available.",
    ),
  ).not.toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "Loading more teams" }),
  ).toBeDisabled();

  await act(async () => {
    currentContinuation.resolve({
      data: { data: { items: [], nextCursor: null } },
    } as unknown as Awaited<ReturnType<typeof getTeams>>);
    await currentContinuation.promise;
  });
});

it("invalidates a stale member continuation success and its finally state when the embedded page changes", async () => {
  const staleContinuation =
    deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const currentContinuation =
    deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const refreshedMember = {
    ...member,
    id: "membership-refreshed",
    userId: "user-refreshed",
    name: "Refreshed member",
    email: "refreshed@example.test",
  };
  const staleMember = {
    ...member,
    id: "membership-stale",
    userId: "user-stale",
    name: "Stale continuation member",
    email: "stale@example.test",
  };
  const currentMember = {
    ...member,
    id: "membership-current",
    userId: "user-current",
    name: "Current continuation member",
    email: "current@example.test",
  };
  jest
    .mocked(getTeamMembers)
    .mockReturnValueOnce(
      staleContinuation.promise as ReturnType<typeof getTeamMembers>,
    )
    .mockReturnValueOnce(
      currentContinuation.promise as ReturnType<typeof getTeamMembers>,
    );
  const view = renderWithMessages(
    <TeamDirectory
      initialPage={{
        items: [
          { ...team, members: { items: [member], nextCursor: "stale-cursor" } },
        ],
        nextCursor: null,
      }}
      organization={{ id: "org-1", canManageTeams: true }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(1));
  view.rerender(
    withMessages(
      <TeamDirectory
        initialPage={{
          items: [
            {
              ...team,
              membersIncluded: true,
              members: {
                items: [refreshedMember],
                nextCursor: "current-cursor",
              },
            },
          ],
          nextCursor: null,
        }}
        organization={{ id: "org-1", canManageTeams: true }}
      />,
    ),
  );
  expect(screen.getByText("Refreshed member")).toBeVisible();
  const currentLoadMore = screen.getByRole("button", {
    name: "Load more members",
  });
  expect(currentLoadMore).toBeEnabled();
  fireEvent.click(currentLoadMore);
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(2));

  await act(async () => {
    staleContinuation.resolve({
      data: {
        data: { items: [staleMember], nextCursor: "stale-result-cursor" },
      },
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
    await staleContinuation.promise;
  });

  expect(
    screen.queryByText("Stale continuation member"),
  ).not.toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "Loading more members" }),
  ).toBeDisabled();

  await act(async () => {
    currentContinuation.resolve({
      data: { data: { items: [currentMember], nextCursor: null } },
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
    await currentContinuation.promise;
  });

  expect(screen.getByText("Current continuation member")).toBeVisible();
});

it("invalidates a stale member continuation error without clearing a newer pending read", async () => {
  const staleContinuation =
    deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const currentContinuation =
    deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  jest
    .mocked(getTeamMembers)
    .mockReturnValueOnce(
      staleContinuation.promise as ReturnType<typeof getTeamMembers>,
    )
    .mockReturnValueOnce(
      currentContinuation.promise as ReturnType<typeof getTeamMembers>,
    );
  const view = renderWithMessages(
    <TeamDirectory
      initialPage={{
        items: [
          { ...team, members: { items: [member], nextCursor: "stale-cursor" } },
        ],
        nextCursor: null,
      }}
      organization={{ id: "org-1", canManageTeams: true }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(1));
  view.rerender(
    withMessages(
      <TeamDirectory
        initialPage={{
          items: [
            {
              ...team,
              membersIncluded: true,
              members: { items: [member], nextCursor: "current-cursor" },
            },
          ],
          nextCursor: null,
        }}
        organization={{ id: "org-1", canManageTeams: true }}
      />,
    ),
  );
  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(2));

  await act(async () => {
    staleContinuation.resolve({
      error: { code: "api_unavailable" },
      response: { status: 503 } as Response,
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
    await staleContinuation.promise;
  });

  expect(
    screen.queryByText("Some team members could not be loaded."),
  ).not.toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "Loading more members" }),
  ).toBeDisabled();

  await act(async () => {
    currentContinuation.resolve({
      data: { data: { items: [], nextCursor: null } },
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
    await currentContinuation.promise;
  });
});

it("reconciles a successful team recovery page and its continuation cursor after a saved create", async () => {
  const created = {
    ...team,
    id: "team-2",
    name: "Design",
    memberCount: 0,
    membersIncluded: true,
    members: { items: [], nextCursor: null },
  };
  jest.mocked(createBrowserTeam).mockResolvedValue({ ok: true, data: created });
  jest
    .mocked(getTeams)
    .mockResolvedValueOnce({
      data: undefined,
      error: { code: "api_unavailable" },
      response: { status: 503 } as Response,
    } as unknown as Awaited<ReturnType<typeof getTeams>>)
    .mockResolvedValueOnce({
      data: {
        data: {
          items: [
            created,
            { ...team, name: "Platform API" },
            { ...created, name: "Design API" },
          ],
          nextCursor: "recovered-next",
        },
      },
    } as unknown as Awaited<ReturnType<typeof getTeams>>);
  renderManager();

  fireEvent.click(screen.getByRole("button", { name: "Create team" }));
  const dialog = screen.getByRole("dialog", { name: "Create team" });
  fireEvent.change(within(dialog).getByLabelText("Team name"), {
    target: { value: "Design" },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Create team" }));
  expect(
    await screen.findByText(
      "The change was saved, but the latest team data could not be loaded.",
    ),
  ).toBeVisible();

  fireEvent.click(screen.getByRole("button", { name: "Retry" }));

  expect(await screen.findByText("Platform API")).toBeVisible();
  expect(screen.getAllByText("Design API")).toHaveLength(1);
  expect(screen.queryByText("Design")).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Load more teams" })).toBeVisible();
});

it("keeps a confirmed create over a stale RSC page when recovery fails", async () => {
  const created = {
    ...team,
    id: "team-created",
    name: "Created locally",
    memberCount: 0,
    membersIncluded: true,
    members: { items: [], nextCursor: null },
  };
  const staleRecovery = deferred<Awaited<ReturnType<typeof getTeams>>>();
  const currentRecovery = deferred<Awaited<ReturnType<typeof getTeams>>>();
  jest.mocked(createBrowserTeam).mockResolvedValue({ ok: true, data: created });
  jest
    .mocked(getTeams)
    .mockReturnValueOnce(staleRecovery.promise as ReturnType<typeof getTeams>)
    .mockReturnValueOnce(
      currentRecovery.promise as ReturnType<typeof getTeams>,
    );
  const view = renderManager();

  fireEvent.click(screen.getByRole("button", { name: "Create team" }));
  const dialog = screen.getByRole("dialog", { name: "Create team" });
  fireEvent.change(within(dialog).getByLabelText("Team name"), {
    target: { value: created.name },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Create team" }));
  expect(await screen.findByText(created.name)).toBeVisible();
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(1));

  view.rerender(
    withMessages(
      <TeamDirectory
        initialPage={{ items: [team], nextCursor: "stale-rsc-cursor" }}
        organization={{ id: "org-1", canManageTeams: true }}
      />,
    ),
  );
  expect(screen.getByText(created.name)).toBeVisible();
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(2));

  await act(async () => {
    staleRecovery.resolve({
      data: { data: { items: [team], nextCursor: "stale-result-cursor" } },
    } as unknown as Awaited<ReturnType<typeof getTeams>>);
    await staleRecovery.promise;
    currentRecovery.resolve({
      error: { code: "api_unavailable" },
      response: { status: 503 } as Response,
    } as unknown as Awaited<ReturnType<typeof getTeams>>);
    await currentRecovery.promise;
  });

  expect(screen.getByText(created.name)).toBeVisible();
  expect(createBrowserTeam).toHaveBeenCalledTimes(1);
});

it("keeps a confirmed rename over a stale RSC row until a newer raw team read acknowledges it", async () => {
  const renamed = { ...team, name: "Product confirmed" };
  const staleRecovery = deferred<Awaited<ReturnType<typeof getTeams>>>();
  const currentRecovery = deferred<Awaited<ReturnType<typeof getTeams>>>();
  jest.mocked(updateBrowserTeam).mockResolvedValue({ ok: true, data: renamed });
  jest
    .mocked(getTeams)
    .mockReturnValueOnce(staleRecovery.promise as ReturnType<typeof getTeams>)
    .mockReturnValueOnce(
      currentRecovery.promise as ReturnType<typeof getTeams>,
    );
  const view = renderManager();

  fireEvent.click(screen.getByRole("button", { name: "Rename Platform" }));
  const dialog = screen.getByRole("dialog", { name: "Rename Platform" });
  fireEvent.change(within(dialog).getByLabelText("Team name"), {
    target: { value: renamed.name },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Rename team" }));
  expect(await screen.findByText(renamed.name)).toBeVisible();
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(1));

  view.rerender(
    withMessages(
      <TeamDirectory
        initialPage={{
          items: [{ ...team, name: "Platform stale RSC" }],
          nextCursor: null,
        }}
        organization={{ id: "org-1", canManageTeams: true }}
      />,
    ),
  );
  expect(screen.getByText(renamed.name)).toBeVisible();
  expect(screen.queryByText("Platform stale RSC")).not.toBeInTheDocument();
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(2));

  await act(async () => {
    staleRecovery.resolve({
      data: {
        data: {
          items: [{ ...team, name: "Platform stale raw GET" }],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getTeams>>);
    await staleRecovery.promise;
    currentRecovery.resolve({
      data: {
        data: {
          items: [{ ...renamed, memberCount: 2 }],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getTeams>>);
    await currentRecovery.promise;
  });

  expect(await screen.findByText("2 members")).toBeVisible();
  expect(screen.getByText(renamed.name)).toBeVisible();
  expect(updateBrowserTeam).toHaveBeenCalledTimes(1);
});

it("lets a causally newer continuation acknowledge an exact created team row", async () => {
  const created = {
    ...team,
    id: "team-created-on-later-page",
    name: "Created confirmation",
    memberCount: 0,
    membersIncluded: true,
    members: { items: [], nextCursor: null },
  };
  const continuation = deferred<Awaited<ReturnType<typeof getTeams>>>();
  jest.mocked(createBrowserTeam).mockResolvedValue({ ok: true, data: created });
  jest
    .mocked(getTeams)
    .mockResolvedValueOnce({
      data: { data: { items: [team], nextCursor: "created-page-cursor" } },
    } as unknown as Awaited<ReturnType<typeof getTeams>>)
    .mockReturnValueOnce(continuation.promise as ReturnType<typeof getTeams>);
  renderManager();

  fireEvent.click(screen.getByRole("button", { name: "Create team" }));
  const dialog = screen.getByRole("dialog", { name: "Create team" });
  fireEvent.change(within(dialog).getByLabelText("Team name"), {
    target: { value: created.name },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Create team" }));
  expect(await screen.findByText(created.name)).toBeVisible();
  fireEvent.click(
    await screen.findByRole("button", { name: "Load more teams" }),
  );

  await act(async () => {
    continuation.resolve({
      data: {
        data: {
          items: [{ ...created, name: "Created from continuation" }],
          nextCursor: null,
        },
      },
    } as unknown as Awaited<ReturnType<typeof getTeams>>);
    await continuation.promise;
  });

  expect(await screen.findByText("Created from continuation")).toBeVisible();
  expect(screen.queryByText(created.name)).not.toBeInTheDocument();
  expect(createBrowserTeam).toHaveBeenCalledTimes(1);
});

it("lets an exact renamed team row on a continuation retire the overlay before later raw changes", async () => {
  const renamed = { ...team, name: "Renamed confirmation" };
  const exactContinuation = deferred<Awaited<ReturnType<typeof getTeams>>>();
  const laterContinuation = deferred<Awaited<ReturnType<typeof getTeams>>>();
  const otherTeam = {
    ...team,
    id: "team-other",
    name: "Other team",
    membersIncluded: true,
    members: { items: [], nextCursor: null },
    memberCount: 0,
  };
  jest.mocked(updateBrowserTeam).mockResolvedValue({ ok: true, data: renamed });
  jest
    .mocked(getTeams)
    .mockResolvedValueOnce({
      data: {
        data: { items: [otherTeam], nextCursor: "renamed-page-cursor" },
      },
    } as unknown as Awaited<ReturnType<typeof getTeams>>)
    .mockReturnValueOnce(
      exactContinuation.promise as ReturnType<typeof getTeams>,
    )
    .mockReturnValueOnce(
      laterContinuation.promise as ReturnType<typeof getTeams>,
    );
  renderManager();

  fireEvent.click(screen.getByRole("button", { name: "Rename Platform" }));
  const dialog = screen.getByRole("dialog", { name: "Rename Platform" });
  fireEvent.change(within(dialog).getByLabelText("Team name"), {
    target: { value: renamed.name },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Rename team" }));
  expect(await screen.findByText(renamed.name)).toBeVisible();
  fireEvent.click(
    await screen.findByRole("button", { name: "Load more teams" }),
  );

  await act(async () => {
    exactContinuation.resolve({
      data: {
        data: { items: [renamed], nextCursor: "later-team-cursor" },
      },
    } as unknown as Awaited<ReturnType<typeof getTeams>>);
    await exactContinuation.promise;
  });
  fireEvent.click(
    await screen.findByRole("button", { name: "Load more teams" }),
  );

  await act(async () => {
    laterContinuation.resolve({
      data: {
        data: {
          items: [{ ...renamed, name: "Renamed after acknowledgement" }],
          nextCursor: null,
        },
      },
    } as unknown as Awaited<ReturnType<typeof getTeams>>);
    await laterContinuation.promise;
  });

  expect(
    await screen.findByText("Renamed after acknowledgement"),
  ).toBeVisible();
  expect(screen.queryByText(renamed.name)).not.toBeInTheDocument();
  expect(updateBrowserTeam).toHaveBeenCalledTimes(1);
});

it("keeps a confirmed delete over a stale RSC row when recovery fails", async () => {
  const staleRecovery = deferred<Awaited<ReturnType<typeof getTeams>>>();
  const currentRecovery = deferred<Awaited<ReturnType<typeof getTeams>>>();
  jest.mocked(deleteBrowserTeam).mockResolvedValue({
    ok: true,
    data: { teamId: team.id },
  });
  jest
    .mocked(getTeams)
    .mockReturnValueOnce(staleRecovery.promise as ReturnType<typeof getTeams>)
    .mockReturnValueOnce(
      currentRecovery.promise as ReturnType<typeof getTeams>,
    );
  const view = renderManager();

  fireEvent.click(screen.getByRole("button", { name: "Delete Platform" }));
  const dialog = screen.getByRole("dialog", { name: "Delete Platform?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Delete team" }));
  await waitFor(() =>
    expect(screen.queryByText("Platform")).not.toBeInTheDocument(),
  );
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(1));

  view.rerender(
    withMessages(
      <TeamDirectory
        initialPage={{ items: [{ ...team }], nextCursor: null }}
        organization={{ id: "org-1", canManageTeams: true }}
      />,
    ),
  );
  expect(screen.queryByText("Platform")).not.toBeInTheDocument();
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(2));

  await act(async () => {
    staleRecovery.resolve({
      data: { data: { items: [team], nextCursor: null } },
    } as Awaited<ReturnType<typeof getTeams>>);
    await staleRecovery.promise;
    currentRecovery.resolve({
      error: { code: "api_unavailable" },
      response: { status: 503 } as Response,
    } as unknown as Awaited<ReturnType<typeof getTeams>>);
    await currentRecovery.promise;
  });

  expect(screen.queryByText("Platform")).not.toBeInTheDocument();
  expect(deleteBrowserTeam).toHaveBeenCalledTimes(1);
});

it("does not acknowledge a confirmed delete from absence on a tail continuation", async () => {
  const staleRscRecovery = deferred<Awaited<ReturnType<typeof getTeams>>>();
  const otherTeam = {
    ...team,
    id: "team-delete-tail-other",
    name: "Other team",
    membersIncluded: true,
    members: { items: [], nextCursor: null },
    memberCount: 0,
  };
  jest.mocked(deleteBrowserTeam).mockResolvedValue({
    ok: true,
    data: { teamId: team.id },
  });
  jest
    .mocked(getTeams)
    .mockResolvedValueOnce({
      data: {
        data: { items: [otherTeam], nextCursor: "delete-tail-cursor" },
      },
    } as unknown as Awaited<ReturnType<typeof getTeams>>)
    .mockResolvedValueOnce({
      data: { data: { items: [], nextCursor: null } },
    } as unknown as Awaited<ReturnType<typeof getTeams>>)
    .mockReturnValueOnce(
      staleRscRecovery.promise as ReturnType<typeof getTeams>,
    );
  const view = renderManager();

  fireEvent.click(screen.getByRole("button", { name: "Delete Platform" }));
  const dialog = screen.getByRole("dialog", { name: "Delete Platform?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Delete team" }));
  fireEvent.click(
    await screen.findByRole("button", { name: "Load more teams" }),
  );
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(2));

  view.rerender(
    withMessages(
      <TeamDirectory
        initialPage={{ items: [{ ...team }], nextCursor: null }}
        organization={{ id: "org-1", canManageTeams: true }}
      />,
    ),
  );

  expect(screen.queryByText(team.name)).not.toBeInTheDocument();
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(3));
  await act(async () => {
    staleRscRecovery.resolve({
      error: { code: "api_unavailable" },
      response: { status: 503 } as Response,
    } as unknown as Awaited<ReturnType<typeof getTeams>>);
    await staleRscRecovery.promise;
  });
  expect(deleteBrowserTeam).toHaveBeenCalledTimes(1);
});

it("keeps a confirmed member add over a stale embedded RSC page until a newer raw member read acknowledges it", async () => {
  const bobCandidate = {
    memberId: "organization-membership-2",
    userId: "user-2",
    name: "Bob confirmed",
    email: "bob@example.test",
    imageUrl: null,
    role: "member" as const,
    joinedAt: "2026-08-01T00:00:00Z",
  };
  const bobMember = {
    ...member,
    id: "team-membership-2",
    userId: bobCandidate.userId,
    name: bobCandidate.name,
    email: bobCandidate.email,
    role: bobCandidate.role,
  };
  const staleRecovery = deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const currentRecovery =
    deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const teamRecovery = deferred<Awaited<ReturnType<typeof getTeams>>>();
  jest.mocked(getTeamMemberCandidates).mockResolvedValue({
    data: { data: { items: [bobCandidate], nextCursor: null } },
  } as Awaited<ReturnType<typeof getTeamMemberCandidates>>);
  jest.mocked(addBrowserTeamMember).mockResolvedValue({
    ok: true,
    data: bobMember,
  });
  jest
    .mocked(getTeamMembers)
    .mockReturnValueOnce(
      staleRecovery.promise as ReturnType<typeof getTeamMembers>,
    )
    .mockReturnValueOnce(
      currentRecovery.promise as ReturnType<typeof getTeamMembers>,
    );
  jest
    .mocked(getTeams)
    .mockReturnValueOnce(teamRecovery.promise as ReturnType<typeof getTeams>);
  const view = renderManager();

  fireEvent.click(
    screen.getByRole("button", { name: "Add member to Platform" }),
  );
  const dialog = screen.getByRole("dialog", { name: "Add member to Platform" });
  fireEvent.change(within(dialog).getByLabelText("Find a workspace member"), {
    target: { value: "bob" },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Search" }));
  fireEvent.click(
    await within(dialog).findByRole("button", { name: "Add Bob confirmed" }),
  );
  await waitFor(() =>
    expect(
      screen.queryByRole("dialog", { name: "Add member to Platform" }),
    ).not.toBeInTheDocument(),
  );
  expect(screen.getByText(bobMember.name)).toBeVisible();
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(1));

  view.rerender(
    withMessages(
      <TeamDirectory
        initialPage={{
          items: [
            {
              ...team,
              membersIncluded: true,
              members: {
                items: [
                  member,
                  { ...bobMember, name: "Bob stale embedded RSC" },
                ],
                nextCursor: null,
              },
            },
          ],
          nextCursor: null,
        }}
        organization={{ id: "org-1", canManageTeams: true }}
      />,
    ),
  );
  expect(screen.getByText(bobMember.name)).toBeVisible();
  expect(screen.queryByText("Bob stale embedded RSC")).not.toBeInTheDocument();
  expect(screen.getByText("2 members")).toBeVisible();
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(2));
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(1));

  await act(async () => {
    staleRecovery.resolve({
      data: { data: { items: [member], nextCursor: null } },
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
    await staleRecovery.promise;
    currentRecovery.resolve({
      data: {
        data: {
          items: [member, { ...bobMember, name: "Bob from raw GET" }],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getTeamMembers>>);
    await currentRecovery.promise;
  });

  expect(await screen.findByText("Bob from raw GET")).toBeVisible();
  expect(addBrowserTeamMember).toHaveBeenCalledTimes(1);
  await act(async () => {
    teamRecovery.resolve({
      error: { code: "api_unavailable" },
      response: { status: 503 } as Response,
    } as unknown as Awaited<ReturnType<typeof getTeams>>);
    await teamRecovery.promise;
  });
});

it("lets a causally newer member continuation acknowledge an exact added membership row", async () => {
  const bobCandidate = {
    memberId: "organization-membership-continuation",
    userId: "user-continuation",
    name: "Bob confirmation",
    email: "bob-continuation@example.test",
    imageUrl: null,
    role: "member" as const,
    joinedAt: "2026-08-01T00:00:00Z",
  };
  const bobMember = {
    ...member,
    id: "team-membership-continuation",
    userId: bobCandidate.userId,
    name: bobCandidate.name,
    email: bobCandidate.email,
    role: bobCandidate.role,
  };
  const continuation = deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  jest.mocked(getTeamMemberCandidates).mockResolvedValue({
    data: { data: { items: [bobCandidate], nextCursor: null } },
  } as Awaited<ReturnType<typeof getTeamMemberCandidates>>);
  jest.mocked(addBrowserTeamMember).mockResolvedValue({
    ok: true,
    data: bobMember,
  });
  jest
    .mocked(getTeamMembers)
    .mockResolvedValueOnce({
      data: { data: { items: [member], nextCursor: "member-page-cursor" } },
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>)
    .mockReturnValueOnce(
      continuation.promise as ReturnType<typeof getTeamMembers>,
    );
  renderManager();

  fireEvent.click(
    screen.getByRole("button", { name: "Add member to Platform" }),
  );
  const dialog = screen.getByRole("dialog", { name: "Add member to Platform" });
  fireEvent.change(within(dialog).getByLabelText("Find a workspace member"), {
    target: { value: "bob" },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Search" }));
  fireEvent.click(
    await within(dialog).findByRole("button", { name: "Add Bob confirmation" }),
  );
  await waitFor(() =>
    expect(
      screen.queryByRole("dialog", { name: "Add member to Platform" }),
    ).not.toBeInTheDocument(),
  );
  expect(screen.getByText(bobMember.name)).toBeVisible();
  fireEvent.click(
    await screen.findByRole("button", { name: "Load more members" }),
  );

  await act(async () => {
    continuation.resolve({
      data: {
        data: {
          items: [{ ...bobMember, name: "Bob from continuation" }],
          nextCursor: null,
        },
      },
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
    await continuation.promise;
  });

  expect(await screen.findByText("Bob from continuation")).toBeVisible();
  expect(screen.queryByText(bobMember.name)).not.toBeInTheDocument();
  expect(addBrowserTeamMember).toHaveBeenCalledTimes(1);
});

it("keeps a confirmed add count over stale RSC data until a newer team projection rebases it", async () => {
  const firstPage = Array.from({ length: 50 }, (_, index) => ({
    ...member,
    id: `membership-add-count-${index}`,
    userId: `user-add-count-${index}`,
    name: `Existing member ${index}`,
    email: `existing-${index}@example.test`,
  }));
  const bobCandidate = {
    memberId: "organization-membership-add-count",
    userId: "user-add-count",
    name: "Bob count confirmation",
    email: "bob-count@example.test",
    imageUrl: null,
    role: "member" as const,
    joinedAt: "2026-08-01T00:00:00Z",
  };
  const bobMember = {
    ...member,
    id: "team-membership-add-count",
    userId: bobCandidate.userId,
    name: bobCandidate.name,
    email: bobCandidate.email,
    role: bobCandidate.role,
  };
  const initialTeam = {
    ...team,
    memberCount: 50,
    membersIncluded: true,
    members: { items: [member], nextCursor: "embedded-add-count" },
  };
  const staleMembers = {
    items: [{ ...member }],
    nextCursor: "stale-embedded-add-count",
  };
  const memberRecovery = deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const newerTeamProjection = deferred<Awaited<ReturnType<typeof getTeams>>>();
  const staleRscMemberRecovery =
    deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  jest.mocked(getTeamMemberCandidates).mockResolvedValue({
    data: { data: { items: [bobCandidate], nextCursor: null } },
  } as Awaited<ReturnType<typeof getTeamMemberCandidates>>);
  jest.mocked(addBrowserTeamMember).mockResolvedValue({
    ok: true,
    data: bobMember,
  });
  jest
    .mocked(getTeamMembers)
    .mockReturnValueOnce(
      memberRecovery.promise as ReturnType<typeof getTeamMembers>,
    )
    .mockReturnValueOnce(
      staleRscMemberRecovery.promise as ReturnType<typeof getTeamMembers>,
    );
  jest
    .mocked(getTeams)
    .mockReturnValueOnce(
      newerTeamProjection.promise as ReturnType<typeof getTeams>,
    );
  const view = renderWithMessages(
    <TeamDirectory
      initialPage={{ items: [initialTeam], nextCursor: null }}
      organization={{ id: "org-1", canManageTeams: true }}
    />,
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Add member to Platform" }),
  );
  const dialog = screen.getByRole("dialog", { name: "Add member to Platform" });
  fireEvent.change(within(dialog).getByLabelText("Find a workspace member"), {
    target: { value: "bob" },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Search" }));
  fireEvent.click(
    await within(dialog).findByRole("button", {
      name: "Add Bob count confirmation",
    }),
  );
  expect(await screen.findByText("51 members")).toBeVisible();
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(1));
  await act(async () => {
    memberRecovery.resolve({
      data: {
        data: { items: firstPage, nextCursor: "add-count-next" },
      },
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
    await memberRecovery.promise;
  });
  expect(
    await screen.findByRole("button", { name: "Load more members" }),
  ).toBeVisible();
  await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));

  view.rerender(
    withMessages(
      <TeamDirectory
        initialPage={{
          items: [{ ...initialTeam, members: staleMembers }],
          nextCursor: null,
        }}
        organization={{ id: "org-1", canManageTeams: true }}
      />,
    ),
  );

  expect(screen.getByText("51 members")).toBeVisible();
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(1));
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(2));
  await act(async () => {
    staleRscMemberRecovery.resolve({
      error: { code: "api_unavailable" },
      response: { status: 503 } as Response,
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
    await staleRscMemberRecovery.promise;
  });
  await act(async () => {
    newerTeamProjection.resolve({
      data: {
        data: {
          items: [{ ...initialTeam, memberCount: 52, members: staleMembers }],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getTeams>>);
    await newerTeamProjection.promise;
  });

  expect(await screen.findByText("52 members")).toBeVisible();
  expect(addBrowserTeamMember).toHaveBeenCalledTimes(1);
});

it("keeps a complete member traversal count over a delayed stale RSC projection until a newer team read", async () => {
  const firstPage = Array.from({ length: 50 }, (_, index) => ({
    ...member,
    id: `membership-remove-count-${index}`,
    userId: `user-remove-count-${index}`,
    name: `Remaining member ${index}`,
    email: `remaining-${index}@example.test`,
  }));
  const tail = Array.from({ length: 2 }, (_, index) => ({
    ...member,
    id: `membership-remove-count-tail-${index}`,
    userId: `user-remove-count-tail-${index}`,
    name: `Concurrent member ${index}`,
    email: `concurrent-${index}@example.test`,
  }));
  const continuation = deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const memberRecovery = deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const newerTeamProjection = deferred<Awaited<ReturnType<typeof getTeams>>>();
  const initialTeam = {
    ...team,
    memberCount: 52,
    membersIncluded: true,
    members: { items: [member], nextCursor: "embedded-remove-count" },
  };
  jest.mocked(removeBrowserTeamMember).mockResolvedValue({
    ok: true,
    data: { teamId: team.id, userId: member.userId },
  });
  jest
    .mocked(getTeamMembers)
    .mockReturnValueOnce(
      memberRecovery.promise as ReturnType<typeof getTeamMembers>,
    )
    .mockReturnValueOnce(
      continuation.promise as ReturnType<typeof getTeamMembers>,
    );
  jest
    .mocked(getTeams)
    .mockReturnValueOnce(
      newerTeamProjection.promise as ReturnType<typeof getTeams>,
    );
  const view = renderWithMessages(
    <TeamDirectory
      initialPage={{ items: [initialTeam], nextCursor: null }}
      organization={{ id: "org-1", canManageTeams: true }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Remove Alice Admin" }));
  expect(await screen.findByText("51 members")).toBeVisible();
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(1));
  await act(async () => {
    memberRecovery.resolve({
      data: {
        data: { items: firstPage, nextCursor: "remove-count-next" },
      },
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
    await memberRecovery.promise;
  });
  await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
  const delayedStaleRscPage = {
    items: [{ ...initialTeam, memberCount: 51 }],
    nextCursor: null,
  };
  fireEvent.click(
    await screen.findByRole("button", { name: "Load more members" }),
  );
  await act(async () => {
    continuation.resolve({
      data: { data: { items: tail, nextCursor: null } },
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
    await continuation.promise;
  });

  expect(await screen.findByText("52 members")).toBeVisible();
  view.rerender(
    withMessages(
      <TeamDirectory
        initialPage={delayedStaleRscPage}
        organization={{ id: "org-1", canManageTeams: true }}
      />,
    ),
  );

  expect(screen.getByText("52 members")).toBeVisible();
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(1));
  await act(async () => {
    newerTeamProjection.resolve({
      data: {
        data: {
          items: [{ ...initialTeam, memberCount: 53 }],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getTeams>>);
    await newerTeamProjection.promise;
  });

  expect(await screen.findByText("53 members")).toBeVisible();
  expect(getTeams).toHaveBeenCalledTimes(1);
  expect(removeBrowserTeamMember).toHaveBeenCalledTimes(1);
});

it("does not let an older overlapping team projection regress an authoritative member traversal count", async () => {
  const firstPage = Array.from({ length: 50 }, (_, index) => ({
    ...member,
    id: `membership-overlap-count-${index}`,
    userId: `user-overlap-count-${index}`,
    name: `Remaining overlap member ${index}`,
    email: `remaining-overlap-${index}@example.test`,
  }));
  const tail = Array.from({ length: 2 }, (_, index) => ({
    ...member,
    id: `membership-overlap-count-tail-${index}`,
    userId: `user-overlap-count-tail-${index}`,
    name: `Concurrent overlap member ${index}`,
    email: `concurrent-overlap-${index}@example.test`,
  }));
  const initialTeam = {
    ...team,
    memberCount: 52,
    membersIncluded: true,
    members: { items: [member], nextCursor: "embedded-overlap-count" },
  };
  const firstMemberPage =
    deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const lastMemberPage = deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const olderTeamProjection = deferred<Awaited<ReturnType<typeof getTeams>>>();
  const newerTeamProjection = deferred<Awaited<ReturnType<typeof getTeams>>>();
  jest.mocked(removeBrowserTeamMember).mockResolvedValue({
    ok: true,
    data: { teamId: team.id, userId: member.userId },
  });
  jest
    .mocked(getTeamMembers)
    .mockReturnValueOnce(
      firstMemberPage.promise as ReturnType<typeof getTeamMembers>,
    )
    .mockReturnValueOnce(
      lastMemberPage.promise as ReturnType<typeof getTeamMembers>,
    );
  jest
    .mocked(getTeams)
    .mockReturnValueOnce(
      olderTeamProjection.promise as ReturnType<typeof getTeams>,
    )
    .mockReturnValueOnce(
      newerTeamProjection.promise as ReturnType<typeof getTeams>,
    );
  renderWithMessages(
    <TeamDirectory
      initialPage={{ items: [initialTeam], nextCursor: "older-team-cursor" }}
      organization={{ id: "org-1", canManageTeams: true }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Remove Alice Admin" }));
  expect(await screen.findByText("51 members")).toBeVisible();
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(1));
  await act(async () => {
    firstMemberPage.resolve({
      data: {
        data: { items: firstPage, nextCursor: "overlap-member-cursor" },
      },
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
    await firstMemberPage.promise;
  });
  await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));

  fireEvent.click(screen.getByRole("button", { name: "Load more teams" }));
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(1));
  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  await act(async () => {
    lastMemberPage.resolve({
      data: { data: { items: tail, nextCursor: null } },
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
    await lastMemberPage.promise;
  });
  expect(await screen.findByText("52 members")).toBeVisible();

  await act(async () => {
    olderTeamProjection.resolve({
      data: {
        data: {
          items: [{ ...initialTeam, memberCount: 51 }],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getTeams>>);
    await olderTeamProjection.promise;
  });

  expect(screen.getByText("52 members")).toBeVisible();
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(2));
  await act(async () => {
    newerTeamProjection.resolve({
      data: {
        data: {
          items: [{ ...initialTeam, memberCount: 53 }],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getTeams>>);
    await newerTeamProjection.promise;
  });

  expect(await screen.findByText("53 members")).toBeVisible();
  expect(getTeams).toHaveBeenCalledTimes(2);
  expect(removeBrowserTeamMember).toHaveBeenCalledTimes(1);
});

it("keeps a confirmed member removal over a stale embedded RSC page when recovery fails", async () => {
  const staleRecovery = deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const currentRecovery =
    deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  jest.mocked(removeBrowserTeamMember).mockResolvedValue({
    ok: true,
    data: { teamId: team.id, userId: member.userId },
  });
  jest
    .mocked(getTeamMembers)
    .mockReturnValueOnce(
      staleRecovery.promise as ReturnType<typeof getTeamMembers>,
    )
    .mockReturnValueOnce(
      currentRecovery.promise as ReturnType<typeof getTeamMembers>,
    );
  const view = renderManager();

  fireEvent.click(screen.getByRole("button", { name: "Remove Alice Admin" }));
  await waitFor(() =>
    expect(screen.queryByText(member.name)).not.toBeInTheDocument(),
  );
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(1));

  view.rerender(
    withMessages(
      <TeamDirectory
        initialPage={{
          items: [
            {
              ...team,
              membersIncluded: true,
              members: { items: [{ ...member }], nextCursor: null },
            },
          ],
          nextCursor: null,
        }}
        organization={{ id: "org-1", canManageTeams: true }}
      />,
    ),
  );
  expect(screen.queryByText(member.name)).not.toBeInTheDocument();
  expect(screen.getByText("0 members")).toBeVisible();
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(2));

  await act(async () => {
    staleRecovery.resolve({
      data: { data: { items: [member], nextCursor: null } },
    } as Awaited<ReturnType<typeof getTeamMembers>>);
    await staleRecovery.promise;
    currentRecovery.resolve({
      error: { code: "api_unavailable" },
      response: { status: 503 } as Response,
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
    await currentRecovery.promise;
  });

  expect(screen.queryByText(member.name)).not.toBeInTheDocument();
  expect(removeBrowserTeamMember).toHaveBeenCalledTimes(1);
});

it("does not acknowledge a confirmed member removal from absence on a tail continuation", async () => {
  const staleRscRecovery =
    deferred<Awaited<ReturnType<typeof getTeamMembers>>>();
  const staleTeamRecovery = deferred<Awaited<ReturnType<typeof getTeams>>>();
  const initialTeam = {
    ...team,
    membersIncluded: true,
    members: { items: [member], nextCursor: "embedded-remove-tail" },
  };
  jest.mocked(removeBrowserTeamMember).mockResolvedValue({
    ok: true,
    data: { teamId: team.id, userId: member.userId },
  });
  jest
    .mocked(getTeamMembers)
    .mockResolvedValueOnce({
      error: { code: "api_unavailable" },
      response: { status: 503 } as Response,
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>)
    .mockResolvedValueOnce({
      data: { data: { items: [], nextCursor: null } },
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>)
    .mockReturnValueOnce(
      staleRscRecovery.promise as ReturnType<typeof getTeamMembers>,
    );
  jest
    .mocked(getTeams)
    .mockReturnValueOnce(
      staleTeamRecovery.promise as ReturnType<typeof getTeams>,
    );
  const view = renderWithMessages(
    <TeamDirectory
      initialPage={{ items: [initialTeam], nextCursor: null }}
      organization={{ id: "org-1", canManageTeams: true }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Remove Alice Admin" }));
  fireEvent.click(
    await screen.findByRole("button", { name: "Load more members" }),
  );
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(2));

  view.rerender(
    withMessages(
      <TeamDirectory
        initialPage={{
          items: [
            {
              ...team,
              membersIncluded: true,
              members: { items: [{ ...member }], nextCursor: null },
            },
          ],
          nextCursor: null,
        }}
        organization={{ id: "org-1", canManageTeams: true }}
      />,
    ),
  );

  expect(screen.queryByText(member.name)).not.toBeInTheDocument();
  await waitFor(() => expect(getTeamMembers).toHaveBeenCalledTimes(3));
  await waitFor(() => expect(getTeams).toHaveBeenCalledTimes(1));
  await act(async () => {
    staleRscRecovery.resolve({
      error: { code: "api_unavailable" },
      response: { status: 503 } as Response,
    } as unknown as Awaited<ReturnType<typeof getTeamMembers>>);
    await staleRscRecovery.promise;
    staleTeamRecovery.resolve({
      error: { code: "api_unavailable" },
      response: { status: 503 } as Response,
    } as unknown as Awaited<ReturnType<typeof getTeams>>);
    await staleTeamRecovery.promise;
  });
  expect(removeBrowserTeamMember).toHaveBeenCalledTimes(1);
});

it("discards candidate search completion after the dialog closes", async () => {
  const pending =
    deferred<Awaited<ReturnType<typeof getTeamMemberCandidates>>>();
  jest
    .mocked(getTeamMemberCandidates)
    .mockReturnValueOnce(
      pending.promise as ReturnType<typeof getTeamMemberCandidates>,
    );
  renderManager();

  fireEvent.click(
    screen.getByRole("button", { name: "Add member to Platform" }),
  );
  let dialog = screen.getByRole("dialog", { name: "Add member to Platform" });
  fireEvent.change(within(dialog).getByLabelText("Find a workspace member"), {
    target: { value: "bob" },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Search" }));
  fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
  pending.resolve({
    data: {
      data: {
        items: [
          {
            memberId: "member-2",
            userId: "user-2",
            name: "Stale Bob",
            email: "bob@example.test",
            imageUrl: null,
            role: "member",
            joinedAt: "2026-08-01T00:00:00Z",
          },
        ],
        nextCursor: null,
      },
    },
  } as Awaited<ReturnType<typeof getTeamMemberCandidates>>);
  await waitFor(() =>
    expect(screen.queryByText("Stale Bob")).not.toBeInTheDocument(),
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Add member to Platform" }),
  );
  dialog = screen.getByRole("dialog", { name: "Add member to Platform" });
  expect(within(dialog).queryByText("Stale Bob")).not.toBeInTheDocument();
  expect(within(dialog).getByRole("button", { name: "Search" })).toBeEnabled();
  expect(
    within(dialog).queryByRole("button", { name: "Searching" }),
  ).not.toBeInTheDocument();
  expect(within(dialog).getByLabelText("Find a workspace member")).toHaveValue(
    "",
  );
});

it("invalidates an in-flight candidate continuation when a successful add closes the dialog", async () => {
  const bob = {
    memberId: "member-2",
    userId: "user-2",
    name: "Bob Member",
    email: "bob@example.test",
    imageUrl: null,
    role: "member" as const,
    joinedAt: "2026-08-01T00:00:00Z",
  };
  const continuation =
    deferred<Awaited<ReturnType<typeof getTeamMemberCandidates>>>();
  jest
    .mocked(getTeamMemberCandidates)
    .mockResolvedValueOnce({
      data: { data: { items: [bob], nextCursor: "candidate-next" } },
    } as Awaited<ReturnType<typeof getTeamMemberCandidates>>)
    .mockReturnValueOnce(
      continuation.promise as ReturnType<typeof getTeamMemberCandidates>,
    );
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
  let dialog = screen.getByRole("dialog", { name: "Add member to Platform" });
  fireEvent.change(within(dialog).getByLabelText("Find a workspace member"), {
    target: { value: "bob" },
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Search" }));
  expect(await within(dialog).findByText("Bob Member")).toBeVisible();
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Load more candidates" }),
  );
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Add Bob Member" }),
  );
  expect(await screen.findByText("Member added to the team.")).toBeVisible();
  expect(
    screen.queryByRole("dialog", { name: "Add member to Platform" }),
  ).not.toBeInTheDocument();

  continuation.resolve({
    data: {
      data: {
        items: [
          {
            ...bob,
            memberId: "member-3",
            userId: "user-3",
            name: "Stale Carol",
          },
        ],
        nextCursor: "stale-next",
      },
    },
  } as Awaited<ReturnType<typeof getTeamMemberCandidates>>);
  await waitFor(() =>
    expect(screen.queryByText("Stale Carol")).not.toBeInTheDocument(),
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Add member to Platform" }),
  );
  dialog = screen.getByRole("dialog", { name: "Add member to Platform" });
  expect(within(dialog).getByRole("button", { name: "Search" })).toBeEnabled();
  expect(within(dialog).getByLabelText("Find a workspace member")).toHaveValue(
    "",
  );
  expect(within(dialog).queryByText("Bob Member")).not.toBeInTheDocument();
  expect(within(dialog).queryByText("Stale Carol")).not.toBeInTheDocument();
  expect(
    within(dialog).queryByRole("button", { name: "Load more candidates" }),
  ).not.toBeInTheDocument();
});

it("discards candidate results when the query changes before completion", async () => {
  const pending =
    deferred<Awaited<ReturnType<typeof getTeamMemberCandidates>>>();
  jest
    .mocked(getTeamMemberCandidates)
    .mockReturnValueOnce(
      pending.promise as ReturnType<typeof getTeamMemberCandidates>,
    );
  renderManager();

  fireEvent.click(
    screen.getByRole("button", { name: "Add member to Platform" }),
  );
  const dialog = screen.getByRole("dialog", { name: "Add member to Platform" });
  const search = within(dialog).getByLabelText("Find a workspace member");
  fireEvent.change(search, { target: { value: "bob" } });
  fireEvent.click(within(dialog).getByRole("button", { name: "Search" }));
  fireEvent.change(search, { target: { value: "carol" } });
  pending.resolve({
    data: {
      data: {
        items: [
          {
            memberId: "member-2",
            userId: "user-2",
            name: "Stale Bob",
            email: "bob@example.test",
            imageUrl: null,
            role: "member",
            joinedAt: "2026-08-01T00:00:00Z",
          },
        ],
        nextCursor: null,
      },
    },
  } as Awaited<ReturnType<typeof getTeamMemberCandidates>>);

  await waitFor(() =>
    expect(within(dialog).queryByText("Stale Bob")).not.toBeInTheDocument(),
  );
  expect(search).toHaveValue("carol");
});

it.each(["constructor", "__proto__", "unknown_team_code"])(
  "renders generic safe copy for a %s team-control problem code",
  async (code) => {
    jest.mocked(createBrowserTeam).mockResolvedValue({
      ok: false,
      failure: { kind: "problem", code, status: 409 },
    });
    renderManager();
    fireEvent.click(screen.getByRole("button", { name: "Create team" }));
    const dialog = screen.getByRole("dialog", { name: "Create team" });
    fireEvent.change(within(dialog).getByLabelText("Team name"), {
      target: { value: "Design" },
    });
    fireEvent.click(
      within(dialog).getByRole("button", { name: "Create team" }),
    );

    expect(
      await within(dialog).findByText(
        "The collaboration request could not be completed.",
      ),
    ).toBeVisible();
  },
);

it.each(["constructor", "__proto__", "unknown_member_code"])(
  "renders generic safe copy for a %s directory mutation problem code",
  async (code) => {
    jest.mocked(removeBrowserTeamMember).mockResolvedValue({
      ok: false,
      failure: { kind: "problem", code, status: 409 },
    });
    renderManager();

    fireEvent.click(screen.getByRole("button", { name: "Remove Alice Admin" }));

    expect(
      await screen.findByText(
        "The collaboration request could not be completed.",
      ),
    ).toBeVisible();
  },
);
