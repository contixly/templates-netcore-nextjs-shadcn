import {
  act,
  fireEvent,
  screen,
  waitFor,
  within,
} from "@testing-library/react";

import { InvitationActivity } from "@/src/features/collaboration/ui/invitation-activity";
import { createBrowserInvitation } from "@/src/lib/api/collaboration/browser/collaboration-mutations";
import { getOrganizationInvitations } from "@/src/lib/api/generated/sdk.gen";
import type { InvitationResponse } from "@/src/lib/api/generated/types.gen";
import { renderWithMessages, withMessages } from "@/test/support/render";

jest.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: jest.fn() }),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/generated/sdk.gen", () => ({
  getOrganizationInvitations: jest.fn(),
}));
jest.mock(
  "@/src/lib/api/collaboration/browser/collaboration-mutations",
  () => ({ createBrowserInvitation: jest.fn() }),
);

const invitation: InvitationResponse = {
  id: "01900000-0000-7000-8000-000000000101",
  organizationId: "org-1",
  organizationName: "Acme",
  canonicalOrganizationKey: "acme",
  teamId: null,
  teamName: null,
  email: "member@example.test",
  role: "member",
  status: "pending",
  displayState: "pending",
  expiresAt: "2026-08-03T12:00:00Z",
  createdAt: "2026-08-01T12:00:00Z",
  inviterId: "user-1",
  inviterName: "Owner",
  invitationPath: "/invite/01900000-0000-7000-8000-000000000101",
};

const listInvitations = jest.mocked(getOrganizationInvitations);
const createInvitation = jest.mocked(createBrowserInvitation);

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((next) => {
    resolve = next;
  });
  return { promise, resolve };
}

function chooseFilter(name: string) {
  fireEvent.click(screen.getByRole("combobox", { name: "Status" }));
  fireEvent.click(screen.getByRole("option", { name }));
}

beforeEach(() => {
  jest.clearAllMocks();
});

it("renders manager activity including team and expired display states", () => {
  renderWithMessages(
    <InvitationActivity
      initialPage={{
        items: [
          invitation,
          {
            ...invitation,
            id: "invite-expired",
            email: "expired@example.test",
            status: "pending",
            displayState: "expired",
            teamId: "team-1",
            teamName: "Platform",
          },
        ],
        nextCursor: null,
      }}
      organization={{ id: "org-1", currentRole: "admin" }}
      teams={[{ id: "team-1", name: "Platform" }]}
    />,
  );

  const activity = screen.getByRole("region", { name: "Invitation activity" });
  expect(activity).toHaveTextContent("member@example.test");
  expect(activity).toHaveTextContent("expired@example.test");
  expect(activity).toHaveTextContent("Expired");
  expect(activity).toHaveTextContent("Platform");
  expect(
    screen.getByRole("button", { name: "Create invitation" }),
  ).toBeVisible();
});

it("requests the expired filter and then pages only within that filter", async () => {
  listInvitations
    .mockResolvedValueOnce({
      data: {
        data: {
          items: [{ ...invitation, displayState: "expired" }],
          nextCursor: "expired-next",
        },
      },
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>)
    .mockResolvedValueOnce({
      data: {
        data: {
          items: [
            {
              ...invitation,
              id: "invite-2",
              email: "second@example.test",
              displayState: "expired",
            },
          ],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>);
  renderWithMessages(
    <InvitationActivity
      initialPage={{ items: [invitation], nextCursor: "all-next" }}
      organization={{ id: "org-1", currentRole: "owner" }}
      teams={[]}
    />,
  );

  fireEvent.click(screen.getByRole("combobox", { name: "Status" }));
  fireEvent.click(screen.getByRole("option", { name: "Expired" }));
  await waitFor(() =>
    expect(listInvitations).toHaveBeenCalledWith(
      expect.objectContaining({
        path: { organizationId: "org-1" },
        query: { status: "expired", limit: 20 },
      }),
    ),
  );
  fireEvent.click(
    await screen.findByRole("button", { name: "Load more invitations" }),
  );
  expect(await screen.findByText("second@example.test")).toBeVisible();
  expect(listInvitations).toHaveBeenLastCalledWith(
    expect.objectContaining({
      path: { organizationId: "org-1" },
      query: { status: "expired", cursor: "expired-next", limit: 20 },
    }),
  );
});

it("retains visible activity when a continuation fails", async () => {
  listInvitations.mockResolvedValue({
    data: undefined,
    error: { code: "private-detail" },
    response: { status: 503 } as Response,
  } as unknown as Awaited<ReturnType<typeof getOrganizationInvitations>>);
  renderWithMessages(
    <InvitationActivity
      initialPage={{ items: [invitation], nextCursor: "next" }}
      organization={{ id: "org-1", currentRole: "owner" }}
      teams={[]}
    />,
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Load more invitations" }),
  );
  expect(
    await screen.findByText(
      "Some invitations could not be loaded. The invitations already shown are still available.",
    ),
  ).toBeVisible();
  expect(screen.getByText(invitation.email)).toBeVisible();
  expect(screen.queryByText("private-detail")).not.toBeInTheDocument();
});

it("clears the previous filter transaction and retries its first page without an old cursor", async () => {
  listInvitations
    .mockResolvedValueOnce({
      data: undefined,
      error: { detail: "private filter failure" },
      response: { status: 503 } as Response,
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>)
    .mockResolvedValueOnce({
      data: {
        data: {
          items: [
            {
              ...invitation,
              id: "expired-2",
              email: "expired@example.test",
              displayState: "expired",
            },
          ],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>);
  renderWithMessages(
    <InvitationActivity
      initialPage={{ items: [invitation], nextCursor: "old-all-cursor" }}
      organization={{ id: "org-1", currentRole: "owner" }}
      teams={[]}
    />,
  );

  chooseFilter("Expired");
  expect(screen.queryByText(invitation.email)).not.toBeInTheDocument();
  expect(
    await screen.findByText(
      "Some invitations could not be loaded. The invitations already shown are still available.",
    ),
  ).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "Load more invitations" }),
  ).not.toBeInTheDocument();

  fireEvent.click(
    screen.getByRole("button", { name: "Retry invitation filter" }),
  );
  expect(await screen.findByText("expired@example.test")).toBeVisible();
  expect(listInvitations).toHaveBeenNthCalledWith(
    1,
    expect.objectContaining({
      query: { status: "expired", limit: 20 },
    }),
  );
  expect(listInvitations).toHaveBeenNthCalledWith(
    2,
    expect.objectContaining({
      query: { status: "expired", limit: 20 },
    }),
  );
});

it("ignores an overlapping older filter response", async () => {
  const expiredRead =
    deferred<Awaited<ReturnType<typeof getOrganizationInvitations>>>();
  listInvitations
    .mockReturnValueOnce(
      expiredRead.promise as ReturnType<typeof getOrganizationInvitations>,
    )
    .mockResolvedValueOnce({
      data: {
        data: {
          items: [
            {
              ...invitation,
              id: "rejected-2",
              email: "rejected@example.test",
              status: "rejected",
              displayState: "rejected",
            },
          ],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>);
  renderWithMessages(
    <InvitationActivity
      initialPage={{ items: [invitation], nextCursor: "all-next" }}
      organization={{ id: "org-1", currentRole: "owner" }}
      teams={[]}
    />,
  );

  chooseFilter("Expired");
  await waitFor(() => expect(listInvitations).toHaveBeenCalledTimes(1));
  chooseFilter("Rejected");
  expect(await screen.findByText("rejected@example.test")).toBeVisible();

  await act(async () => {
    expiredRead.resolve({
      data: {
        data: {
          items: [
            {
              ...invitation,
              id: "expired-stale",
              email: "stale-expired@example.test",
              displayState: "expired",
            },
          ],
          nextCursor: "stale-cursor",
        },
      },
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>);
    await expiredRead.promise;
  });

  expect(screen.getByText("rejected@example.test")).toBeVisible();
  expect(
    screen.queryByText("stale-expired@example.test"),
  ).not.toBeInTheDocument();
  expect(listInvitations).toHaveBeenNthCalledWith(
    2,
    expect.objectContaining({
      query: { status: "rejected", limit: 20 },
    }),
  );
});

it("keeps a confirmed pending invitation over an older filter read and only overlays matching filters", async () => {
  const olderPendingRead =
    deferred<Awaited<ReturnType<typeof getOrganizationInvitations>>>();
  const latestPendingRead =
    deferred<Awaited<ReturnType<typeof getOrganizationInvitations>>>();
  const created = {
    ...invitation,
    id: "01900000-0000-7000-8000-000000000102",
    email: "created@example.test",
  };
  createInvitation.mockResolvedValue({ ok: true, data: created });
  listInvitations
    .mockReturnValueOnce(
      olderPendingRead.promise as ReturnType<typeof getOrganizationInvitations>,
    )
    .mockReturnValueOnce(
      latestPendingRead.promise as ReturnType<
        typeof getOrganizationInvitations
      >,
    )
    .mockResolvedValueOnce({
      data: {
        data: {
          items: [
            {
              ...invitation,
              id: "accepted-1",
              email: "accepted@example.test",
              status: "accepted",
              displayState: "accepted",
            },
          ],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>);
  renderWithMessages(
    <InvitationActivity
      initialPage={{ items: [invitation], nextCursor: "all-next" }}
      organization={{ id: "org-1", currentRole: "owner" }}
      teams={[]}
    />,
  );

  chooseFilter("Pending");
  await waitFor(() => expect(listInvitations).toHaveBeenCalledTimes(1));
  fireEvent.click(screen.getByRole("button", { name: "Create invitation" }));
  const dialog = screen.getByRole("dialog", {
    name: "Invite a workspace member",
  });
  fireEvent.change(within(dialog).getByLabelText("Email address"), {
    target: { value: created.email },
  });
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Create invitation" }),
  );

  expect(await screen.findByText(created.email)).toBeVisible();
  expect(
    within(dialog).queryByRole("button", { name: "Create invitation" }),
  ).not.toBeInTheDocument();
  expect(createInvitation).toHaveBeenCalledTimes(1);

  await act(async () => {
    olderPendingRead.resolve({
      data: { data: { items: [invitation], nextCursor: "stale-next" } },
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>);
    await olderPendingRead.promise;
  });
  await waitFor(() => expect(listInvitations).toHaveBeenCalledTimes(2));
  expect(screen.getByText(created.email)).toBeVisible();

  await act(async () => {
    latestPendingRead.resolve({
      data: { data: { items: [invitation], nextCursor: null } },
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>);
    await latestPendingRead.promise;
  });
  expect(screen.getByText(created.email)).toBeVisible();

  fireEvent.click(within(dialog).getByRole("button", { name: "Close" }));
  chooseFilter("Accepted");
  expect(await screen.findByText("accepted@example.test")).toBeVisible();
  expect(screen.queryByText(created.email)).not.toBeInTheDocument();
});

it("keeps an unacknowledged create over a stale RSC page and queues a current GET", async () => {
  const preReplacementRecovery =
    deferred<Awaited<ReturnType<typeof getOrganizationInvitations>>>();
  const postReplacementRecovery =
    deferred<Awaited<ReturnType<typeof getOrganizationInvitations>>>();
  const created = {
    ...invitation,
    id: "01900000-0000-7000-8000-000000000103",
    email: "rsc-created@example.test",
  };
  createInvitation.mockResolvedValue({ ok: true, data: created });
  listInvitations
    .mockReturnValueOnce(
      preReplacementRecovery.promise as ReturnType<
        typeof getOrganizationInvitations
      >,
    )
    .mockReturnValueOnce(
      postReplacementRecovery.promise as ReturnType<
        typeof getOrganizationInvitations
      >,
    );
  const view = renderWithMessages(
    <InvitationActivity
      initialPage={{ items: [invitation], nextCursor: "initial-next" }}
      organization={{ id: "org-1", currentRole: "owner" }}
      teams={[]}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Create invitation" }));
  const dialog = screen.getByRole("dialog", {
    name: "Invite a workspace member",
  });
  fireEvent.change(within(dialog).getByLabelText("Email address"), {
    target: { value: created.email },
  });
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Create invitation" }),
  );
  expect(await screen.findByText(created.email)).toBeVisible();
  await waitFor(() => expect(listInvitations).toHaveBeenCalledTimes(1));

  view.rerender(
    withMessages(
      <InvitationActivity
        initialPage={{ items: [invitation], nextCursor: "stale-rsc-next" }}
        organization={{ id: "org-1", currentRole: "owner" }}
        teams={[]}
      />,
    ),
  );

  expect(screen.getByText(created.email)).toBeVisible();
  await waitFor(() => expect(listInvitations).toHaveBeenCalledTimes(2));
  expect(listInvitations).toHaveBeenLastCalledWith(
    expect.objectContaining({ query: { limit: 20 } }),
  );

  await act(async () => {
    preReplacementRecovery.resolve({
      data: { data: { items: [invitation], nextCursor: "older-next" } },
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>);
    await preReplacementRecovery.promise;
  });
  expect(screen.getByText(created.email)).toBeVisible();

  await act(async () => {
    postReplacementRecovery.resolve({
      data: { data: { items: [invitation], nextCursor: null } },
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>);
    await postReplacementRecovery.promise;
  });
  expect(screen.getByText(created.email)).toBeVisible();
  expect(createInvitation).toHaveBeenCalledTimes(1);
});

it("does not let an unrelated continuation acknowledge and clear a confirmed overlay", async () => {
  const created = {
    ...invitation,
    id: "01900000-0000-7000-8000-000000000104",
    email: "continued-created@example.test",
  };
  createInvitation.mockResolvedValue({ ok: true, data: created });
  listInvitations
    .mockResolvedValueOnce({
      data: {
        data: { items: [invitation], nextCursor: "continuation-cursor" },
      },
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>)
    .mockResolvedValueOnce({
      data: {
        data: {
          items: [
            {
              ...invitation,
              id: "continuation-only",
              email: "continuation@example.test",
            },
          ],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>)
    .mockResolvedValueOnce({
      data: {
        data: {
          items: [
            {
              ...invitation,
              id: "accepted-after-continuation",
              email: "accepted-after-continuation@example.test",
              status: "accepted",
              displayState: "accepted",
            },
          ],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>)
    .mockResolvedValueOnce({
      data: { data: { items: [invitation], nextCursor: null } },
    } as Awaited<ReturnType<typeof getOrganizationInvitations>>);
  renderWithMessages(
    <InvitationActivity
      initialPage={{ items: [invitation], nextCursor: null }}
      organization={{ id: "org-1", currentRole: "owner" }}
      teams={[]}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Create invitation" }));
  const dialog = screen.getByRole("dialog", {
    name: "Invite a workspace member",
  });
  fireEvent.change(within(dialog).getByLabelText("Email address"), {
    target: { value: created.email },
  });
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Create invitation" }),
  );
  expect(await screen.findByText(created.email)).toBeVisible();
  await waitFor(() => expect(listInvitations).toHaveBeenCalledTimes(1));
  fireEvent.click(within(dialog).getByRole("button", { name: "Close" }));
  await waitFor(() =>
    expect(
      screen.queryByRole("dialog", { name: "Invite a workspace member" }),
    ).not.toBeInTheDocument(),
  );

  fireEvent.click(
    await screen.findByRole("button", { name: "Load more invitations" }),
  );
  expect(await screen.findByText("continuation@example.test")).toBeVisible();
  expect(screen.getByText(created.email)).toBeVisible();

  chooseFilter("Accepted");
  expect(
    await screen.findByText("accepted-after-continuation@example.test"),
  ).toBeVisible();
  expect(screen.queryByText(created.email)).not.toBeInTheDocument();

  chooseFilter("Pending");
  await waitFor(() => expect(listInvitations).toHaveBeenCalledTimes(4));
  expect(screen.getByText(created.email)).toBeVisible();
  expect(createInvitation).toHaveBeenCalledTimes(1);
});
