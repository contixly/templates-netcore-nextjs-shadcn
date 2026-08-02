import {
  act,
  fireEvent,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { Activity } from "react";
import { renderToString } from "react-dom/server";

import { OrganizationMemberDirectory } from "@/src/components/organizations/organization-member-directory";
import {
  addBrowserOrganizationMember,
  updateBrowserOrganizationMemberRole,
} from "@/src/lib/api/organizations/browser/organization-mutations";
import { getOrganizationMembers } from "@/src/lib/api/generated/sdk.gen";
import type {
  OrganizationDetailResponse,
  OrganizationMemberPageResponse,
  OrganizationMemberResponse,
} from "@/src/lib/api/generated/types.gen";
import { renderWithMessages, withMessages } from "@/test/support/render";

const organizationControlReadyAttribute =
  "data-organization-control-interaction-ready";

jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/organizations/browser/organization-mutations", () => ({
  addBrowserOrganizationMember: jest.fn(),
  updateBrowserOrganizationMemberRole: jest.fn(),
}));
jest.mock("@/src/lib/api/generated/sdk.gen", () => ({
  getOrganizationMembers: jest.fn(),
}));

const getMembers = jest.mocked(getOrganizationMembers);
const addMember = jest.mocked(addBrowserOrganizationMember);
const updateRole = jest.mocked(updateBrowserOrganizationMemberRole);
const currentUserId = "01900000-0000-7000-8000-000000000020";
const organization = {
  id: "01900000-0000-7000-8000-000000000010",
  name: "Acme",
  slug: "acme",
  canonicalKey: "acme",
  createdAt: "2026-07-30T10:00:00Z",
  updatedAt: "2026-07-30T10:00:00Z",
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
  allowedEmailDomains: ["example.com"],
} satisfies OrganizationDetailResponse;
const currentMember = {
  id: "01900000-0000-7000-8000-000000000030",
  userId: currentUserId,
  name: "Current User",
  email: "current@example.com",
  imageUrl: null,
  role: "owner",
  joinedAt: "2026-07-29T10:00:00Z",
  emailDomain: "example.com",
  isOutsideAllowedEmailDomains: false,
} satisfies OrganizationMemberResponse;
const currentActor = {
  userId: currentMember.userId,
  name: currentMember.name,
  email: currentMember.email,
  role: currentMember.role,
  isOutsideAllowedEmailDomains: currentMember.isOutsideAllowedEmailDomains,
};
const otherMember = {
  id: "01900000-0000-7000-8000-000000000031",
  userId: "01900000-0000-7000-8000-000000000021",
  name: "Other User",
  email: "other@external.test",
  imageUrl: null,
  role: "member",
  joinedAt: "2026-07-30T10:00:00Z",
  emailDomain: "external.test",
  isOutsideAllowedEmailDomains: true,
} satisfies OrganizationMemberResponse;
const initialPage = {
  items: [currentMember, otherMember],
  nextCursor: "cursor-next",
} satisfies OrganizationMemberPageResponse;

beforeEach(() => {
  jest.resetAllMocks();
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((next) => {
    resolve = next;
  });
  return { promise, resolve };
}

function memberPageResult(
  items: OrganizationMemberResponse[],
  nextCursor: string | null,
) {
  return {
    data: {
      data: {
        items,
        nextCursor,
      },
    },
  } as Awaited<ReturnType<typeof getOrganizationMembers>>;
}

it("separates the current actor, preserves returned order, and never renders member removal", () => {
  const laterMember = {
    ...otherMember,
    id: "01900000-0000-7000-8000-000000000032",
    userId: "01900000-0000-7000-8000-000000000022",
    name: "Later User",
    email: "later@example.com",
    isOutsideAllowedEmailDomains: false,
  };
  renderWithMessages(
    <OrganizationMemberDirectory
      currentActor={currentActor}
      initialPage={{
        items: [currentMember, otherMember, laterMember],
        nextCursor: null,
      }}
      organization={organization}
    />,
  );

  const ownAccess = screen.getByRole("region", { name: "Your access" });
  expect(within(ownAccess).getByText("Current User")).toBeVisible();
  expect(within(ownAccess).queryByRole("combobox")).not.toBeInTheDocument();

  const others = screen.getByRole("region", { name: "Other members" });
  const rows = within(others).getAllByRole("article");
  expect(rows[0]).toHaveTextContent("Other User");
  expect(rows[1]).toHaveTextContent("Later User");
  expect(within(others).queryByText("Current User")).not.toBeInTheDocument();
  expect(screen.getByText("Outside domain policy")).toBeVisible();
  expect(
    screen.queryByRole("button", { name: /remove|delete member/i }),
  ).not.toBeInTheDocument();
});

it("shows the compact current actor even when the actor is beyond the first member page", () => {
  renderWithMessages(
    <OrganizationMemberDirectory
      currentActor={currentActor}
      initialPage={{ items: [otherMember], nextCursor: "cursor-next" }}
      organization={organization}
    />,
  );

  expect(
    within(screen.getByRole("region", { name: "Your access" })).getByText(
      "Current User",
    ),
  ).toBeVisible();
  expect(
    within(screen.getByRole("region", { name: "Other members" })).getByText(
      "Other User",
    ),
  ).toBeVisible();
});

it("does not offer owner assignment or owner mutation to an admin", () => {
  renderWithMessages(
    <OrganizationMemberDirectory
      currentActor={{ ...currentActor, role: "admin" }}
      initialPage={{
        items: [
          { ...currentMember, role: "admin" },
          otherMember,
          {
            ...otherMember,
            id: "owner-member",
            userId: "owner-user",
            name: "Workspace Owner",
            role: "owner",
          },
        ],
        nextCursor: null,
      }}
      organization={{ ...organization, currentRole: "admin" }}
    />,
  );

  expect(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  ).toBeVisible();
  expect(
    screen.queryByRole("combobox", { name: "Role for Workspace Owner" }),
  ).not.toBeInTheDocument();

  fireEvent.click(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  );
  expect(
    screen.queryByRole("option", { name: "Owner" }),
  ).not.toBeInTheDocument();
});

it("loads the next opaque cursor, appends members, and deduplicates ids", async () => {
  const nextMember = {
    ...otherMember,
    id: "01900000-0000-7000-8000-000000000032",
    userId: "01900000-0000-7000-8000-000000000022",
    name: "Next User",
  };
  getMembers.mockResolvedValue({
    data: {
      data: {
        items: [otherMember, nextMember],
        nextCursor: null,
      },
    },
  } as Awaited<ReturnType<typeof getOrganizationMembers>>);
  renderWithMessages(
    <OrganizationMemberDirectory
      currentActor={currentActor}
      initialPage={initialPage}
      organization={organization}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));

  await waitFor(() => {
    expect(getMembers).toHaveBeenCalledWith({
      client: { id: "browser-client" },
      cache: "no-store",
      path: { organizationId: organization.id },
      query: { cursor: "cursor-next" },
      signal: expect.anything(),
    });
  });
  await waitFor(() => {
    expect(screen.getAllByRole("article")).toHaveLength(2);
  });
  expect(screen.getByText("Next User")).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "Load more members" }),
  ).not.toBeInTheDocument();
});

it("reconciles a refreshed first page while preserving an active continuation and its loaded tail cursor", async () => {
  const tailMember = {
    ...otherMember,
    id: "01900000-0000-7000-8000-000000000032",
    userId: "01900000-0000-7000-8000-000000000022",
    name: "Tail User",
    email: "tail@example.com",
    isOutsideAllowedEmailDomains: false,
  };
  const pendingMember = {
    ...otherMember,
    id: "01900000-0000-7000-8000-000000000033",
    userId: "01900000-0000-7000-8000-000000000023",
    name: "Pending Tail User",
    email: "pending-tail@example.com",
    isOutsideAllowedEmailDomains: false,
  };
  const freshFirstMember = {
    ...otherMember,
    id: "01900000-0000-7000-8000-000000000034",
    userId: "01900000-0000-7000-8000-000000000024",
    name: "Fresh First User",
    email: "fresh-first@example.com",
    isOutsideAllowedEmailDomains: false,
  };
  const refreshedMember = {
    ...otherMember,
    name: "Refreshed Other User",
    email: "refreshed-other@example.com",
    role: "admin" as const,
    isOutsideAllowedEmailDomains: false,
  };
  const pendingContinuation =
    deferred<Awaited<ReturnType<typeof getOrganizationMembers>>>();
  getMembers
    .mockResolvedValueOnce(
      memberPageResult([otherMember, tailMember], "cursor-loaded-tail"),
    )
    .mockImplementationOnce(() => pendingContinuation.promise as never)
    .mockResolvedValueOnce(memberPageResult([], null));
  const view = renderWithMessages(
    <OrganizationMemberDirectory
      currentActor={currentActor}
      initialPage={initialPage}
      organization={organization}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  expect(await screen.findByText("Tail User")).toBeVisible();

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  await waitFor(() => expect(getMembers).toHaveBeenCalledTimes(2));
  expect(
    screen.getByRole("button", { name: "Loading members" }),
  ).toBeDisabled();

  view.rerender(
    withMessages(
      <OrganizationMemberDirectory
        currentActor={currentActor}
        initialPage={{
          items: [currentMember, freshFirstMember, refreshedMember],
          nextCursor: "cursor-incoming-first",
        }}
        organization={organization}
      />,
    ),
  );

  const refreshedRows = within(
    screen.getByRole("region", { name: "Other members" }),
  ).getAllByRole("article");
  expect(refreshedRows[0]).toHaveTextContent("Fresh First User");
  expect(refreshedRows[1]).toHaveTextContent("Refreshed Other User");
  expect(refreshedRows[2]).toHaveTextContent("Tail User");
  expect(
    screen.getByRole("combobox", { name: "Role for Refreshed Other User" }),
  ).toHaveTextContent("Administrator");
  expect(
    screen.queryByRole("article", { name: "Other User workspace member" }),
  ).not.toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "Loading members" }),
  ).toBeDisabled();

  await act(async () => {
    pendingContinuation.resolve(
      memberPageResult([pendingMember], "cursor-after-active-read"),
    );
  });

  expect(await screen.findByText("Pending Tail User")).toBeVisible();
  expect(screen.getByText("Refreshed Other User")).toBeVisible();
  expect(screen.queryByText("Other User")).not.toBeInTheDocument();

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  await waitFor(() => {
    expect(getMembers).toHaveBeenNthCalledWith(3, {
      client: { id: "browser-client" },
      cache: "no-store",
      path: { organizationId: organization.id },
      query: { cursor: "cursor-after-active-read" },
      signal: expect.anything(),
    });
  });
});

it("keeps member pagination unavailable in server HTML until its boundary hydrates", async () => {
  getMembers.mockResolvedValue(memberPageResult([], null));
  const directory = (
    <OrganizationMemberDirectory
      currentActor={currentActor}
      initialPage={initialPage}
      organization={organization}
    />
  );
  const serverMarkup = renderToString(withMessages(directory));
  const serverDocument = new DOMParser().parseFromString(
    serverMarkup,
    "text/html",
  );
  const serverLoadMore = Array.from(
    serverDocument.querySelectorAll("button"),
  ).find((button) => button.textContent?.includes("Load more members"));

  expect(serverLoadMore?.hasAttribute("disabled")).toBe(true);
  expect(serverLoadMore?.getAttribute(organizationControlReadyAttribute)).toBe(
    null,
  );

  renderWithMessages(directory);
  const loadMore = screen.getByRole("button", {
    name: "Load more members",
  });
  await waitFor(() => {
    expect(loadMore).toHaveAttribute(organizationControlReadyAttribute, "true");
  });
  expect(loadMore).toBeEnabled();

  fireEvent.click(loadMore);

  await waitFor(() => {
    expect(getMembers).toHaveBeenCalledWith({
      client: { id: "browser-client" },
      cache: "no-store",
      path: { organizationId: organization.id },
      query: { cursor: "cursor-next" },
      signal: expect.anything(),
    });
  });
});

it("cancels an Activity-hidden load-more read and restores a usable continuation on reveal", async () => {
  const pendingContinuation =
    deferred<Awaited<ReturnType<typeof getOrganizationMembers>>>();
  getMembers
    .mockImplementationOnce(() => pendingContinuation.promise as never)
    .mockResolvedValueOnce(memberPageResult([], null));
  const directory = (
    <OrganizationMemberDirectory
      currentActor={currentActor}
      initialPage={initialPage}
      organization={organization}
    />
  );
  const view = renderWithMessages(
    <Activity mode="visible">{directory}</Activity>,
  );

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  await waitFor(() => expect(getMembers).toHaveBeenCalledTimes(1));
  const hiddenSignal = (
    getMembers.mock.calls[0]?.[0] as { signal: AbortSignal }
  ).signal;
  expect(hiddenSignal.aborted).toBe(false);

  view.rerender(withMessages(<Activity mode="hidden">{directory}</Activity>));
  await waitFor(() => expect(hiddenSignal.aborted).toBe(true));

  view.rerender(withMessages(<Activity mode="visible">{directory}</Activity>));
  const retryLoadMore = await screen.findByRole("button", {
    name: "Load more members",
  });
  await waitFor(() => expect(retryLoadMore).toBeEnabled());
  expect(
    screen.queryByText("Members could not be loaded."),
  ).not.toBeInTheDocument();

  fireEvent.click(retryLoadMore);
  await waitFor(() => expect(getMembers).toHaveBeenCalledTimes(2));
});

it("retains a confirmed role when refresh fails and retry performs GET only", async () => {
  const confirmed = { ...otherMember, role: "admin" as const };
  updateRole.mockResolvedValue({ ok: true, data: confirmed });
  getMembers
    .mockResolvedValueOnce({
      error: {
        code: "internal_error",
        traceId: "trace-members-refresh",
      },
      response: { status: 500 } as Response,
    } as Awaited<ReturnType<typeof getOrganizationMembers>>)
    .mockResolvedValueOnce({
      data: {
        data: {
          items: [currentMember, confirmed],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getOrganizationMembers>>);
  renderWithMessages(
    <OrganizationMemberDirectory
      currentActor={currentActor}
      initialPage={{ ...initialPage, nextCursor: null }}
      organization={organization}
    />,
  );

  fireEvent.click(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  );
  fireEvent.click(screen.getByRole("option", { name: "Administrator" }));

  await waitFor(() => {
    expect(updateRole).toHaveBeenCalledTimes(1);
  });
  const refreshFailure = (
    await screen.findByText(
      "The member change was saved, but the directory could not be refreshed.",
    )
  ).closest('[role="alert"]');
  expect(refreshFailure).toHaveTextContent("trace-members-refresh");
  expect(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  ).toHaveTextContent("Administrator");

  fireEvent.click(
    screen.getByRole("button", { name: "Retry member directory refresh" }),
  );

  expect(await screen.findByRole("status")).toHaveTextContent(
    "Member role updated.",
  );
  expect(getMembers).toHaveBeenCalledTimes(2);
  expect(updateRole).toHaveBeenCalledTimes(1);
  expect(getMembers).toHaveBeenNthCalledWith(2, {
    client: { id: "browser-client" },
    cache: "no-store",
    path: { organizationId: organization.id },
    signal: expect.anything(),
  });
});

it("cancels an Activity-hidden recovery read and keeps its GET-only retry usable", async () => {
  const confirmed = { ...otherMember, role: "admin" as const };
  const pendingRecovery =
    deferred<Awaited<ReturnType<typeof getOrganizationMembers>>>();
  updateRole.mockResolvedValue({ ok: true, data: confirmed });
  getMembers
    .mockResolvedValueOnce({
      error: {
        code: "internal_error",
        traceId: "trace-hidden-recovery",
      },
      response: { status: 500 } as Response,
    } as Awaited<ReturnType<typeof getOrganizationMembers>>)
    .mockImplementationOnce(() => pendingRecovery.promise as never)
    .mockResolvedValueOnce(memberPageResult([currentMember, confirmed], null));
  const directory = (
    <OrganizationMemberDirectory
      currentActor={currentActor}
      initialPage={{ ...initialPage, nextCursor: null }}
      organization={organization}
    />
  );
  const view = renderWithMessages(
    <Activity mode="visible">{directory}</Activity>,
  );

  fireEvent.click(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  );
  fireEvent.click(screen.getByRole("option", { name: "Administrator" }));
  const retry = await screen.findByRole("button", {
    name: "Retry member directory refresh",
  });
  fireEvent.click(retry);
  await waitFor(() => expect(getMembers).toHaveBeenCalledTimes(2));
  const hiddenSignal = (
    getMembers.mock.calls[1]?.[0] as { signal: AbortSignal }
  ).signal;
  expect(hiddenSignal.aborted).toBe(false);

  view.rerender(withMessages(<Activity mode="hidden">{directory}</Activity>));
  await waitFor(() => expect(hiddenSignal.aborted).toBe(true));

  view.rerender(withMessages(<Activity mode="visible">{directory}</Activity>));
  const revealedRetry = await screen.findByRole("button", {
    name: "Retry member directory refresh",
  });
  await waitFor(() => expect(revealedRetry).toBeEnabled());
  fireEvent.click(revealedRetry);

  expect(await screen.findByRole("status")).toHaveTextContent(
    "Member role updated.",
  );
  expect(getMembers).toHaveBeenCalledTimes(3);
  expect(updateRole).toHaveBeenCalledTimes(1);
});

it("keeps a confirmed role overlay and active mutation refresh across later server-page props", async () => {
  const confirmed = { ...otherMember, role: "admin" as const };
  const freshServerMember = {
    ...otherMember,
    id: "01900000-0000-7000-8000-000000000036",
    userId: "01900000-0000-7000-8000-000000000026",
    name: "Fresh Server User",
    email: "fresh-server@example.com",
    isOutsideAllowedEmailDomains: false,
  };
  const staleServerMember = {
    ...otherMember,
    name: "Stale Server User",
    email: "stale-server@example.com",
  };
  const authoritative = {
    ...confirmed,
    name: "Authoritative Other User",
    email: "authoritative@example.com",
    role: "owner" as const,
    isOutsideAllowedEmailDomains: false,
  };
  const mutationRefresh =
    deferred<Awaited<ReturnType<typeof getOrganizationMembers>>>();
  updateRole.mockResolvedValue({ ok: true, data: confirmed });
  getMembers.mockImplementationOnce(() => mutationRefresh.promise as never);
  const view = renderWithMessages(
    <OrganizationMemberDirectory
      currentActor={currentActor}
      initialPage={{ ...initialPage, nextCursor: null }}
      organization={organization}
    />,
  );

  fireEvent.click(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  );
  fireEvent.click(screen.getByRole("option", { name: "Administrator" }));

  await waitFor(() => expect(getMembers).toHaveBeenCalledTimes(1));
  expect(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  ).toHaveTextContent("Administrator");
  expect(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  ).toBeDisabled();

  view.rerender(
    withMessages(
      <OrganizationMemberDirectory
        currentActor={currentActor}
        initialPage={{
          items: [currentMember, freshServerMember, staleServerMember],
          nextCursor: null,
        }}
        organization={organization}
      />,
    ),
  );

  const staleProjectionRows = within(
    screen.getByRole("region", { name: "Other members" }),
  ).getAllByRole("article");
  expect(staleProjectionRows[0]).toHaveTextContent("Fresh Server User");
  expect(staleProjectionRows[1]).toHaveTextContent("Other User");
  expect(screen.queryByText("Stale Server User")).not.toBeInTheDocument();
  expect(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  ).toHaveTextContent("Administrator");
  expect(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  ).toBeDisabled();

  view.rerender(
    withMessages(
      <OrganizationMemberDirectory
        currentActor={currentActor}
        initialPage={{
          items: [currentMember, freshServerMember],
          nextCursor: null,
        }}
        organization={organization}
      />,
    ),
  );

  const overlayRows = within(
    screen.getByRole("region", { name: "Other members" }),
  ).getAllByRole("article");
  expect(overlayRows[0]).toHaveTextContent("Fresh Server User");
  expect(overlayRows[1]).toHaveTextContent("Other User");
  expect(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  ).toBeDisabled();

  await act(async () => {
    mutationRefresh.resolve(
      memberPageResult([currentMember, authoritative, freshServerMember], null),
    );
  });

  expect(await screen.findByRole("status")).toHaveTextContent(
    "Member role updated.",
  );
  expect(screen.getByText("Authoritative Other User")).toBeVisible();
  expect(screen.queryByText("Other User")).not.toBeInTheDocument();
  await waitFor(() => {
    expect(
      screen.getByRole("combobox", {
        name: "Role for Authoritative Other User",
      }),
    ).toBeEnabled();
  });
});

it("replaces the refreshed first page order, preserves loaded progress, and overlays the confirmed role", async () => {
  const nextMember = {
    ...otherMember,
    id: "01900000-0000-7000-8000-000000000032",
    userId: "01900000-0000-7000-8000-000000000022",
    name: "Next User",
    email: "next@example.com",
    isOutsideAllowedEmailDomains: false,
  };
  const confirmed = { ...otherMember, role: "admin" as const };
  updateRole.mockResolvedValue({ ok: true, data: confirmed });
  getMembers
    .mockResolvedValueOnce(memberPageResult([nextMember], "cursor-tail"))
    .mockResolvedValueOnce(
      memberPageResult(
        [currentMember, nextMember],
        "cursor-refreshed-first-page",
      ),
    )
    .mockResolvedValueOnce(memberPageResult([], null));
  renderWithMessages(
    <OrganizationMemberDirectory
      currentActor={currentActor}
      initialPage={initialPage}
      organization={organization}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  expect(await screen.findByText("Next User")).toBeVisible();

  fireEvent.click(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  );
  fireEvent.click(screen.getByRole("option", { name: "Administrator" }));

  await waitFor(() => {
    const rows = within(
      screen.getByRole("region", { name: "Other members" }),
    ).getAllByRole("article");
    expect(rows[0]).toHaveTextContent("Next User");
    expect(rows[1]).toHaveTextContent("Other User");
  });
  expect(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  ).toHaveTextContent("Administrator");

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  await waitFor(() => {
    expect(getMembers).toHaveBeenNthCalledWith(3, {
      client: { id: "browser-client" },
      cache: "no-store",
      path: { organizationId: organization.id },
      query: { cursor: "cursor-tail" },
      signal: expect.anything(),
    });
  });
});

it("applies a post-mutation authoritative refresh and ignores an older delayed load-more response", async () => {
  const delayedLoadMore =
    deferred<Awaited<ReturnType<typeof getOrganizationMembers>>>();
  const mutationRefresh =
    deferred<Awaited<ReturnType<typeof getOrganizationMembers>>>();
  const staleMember = {
    ...otherMember,
    id: "01900000-0000-7000-8000-000000000033",
    userId: "01900000-0000-7000-8000-000000000023",
    name: "Stale Delayed User",
  };
  updateRole.mockResolvedValue({
    ok: true,
    data: { ...otherMember, role: "admin" },
  });
  getMembers
    .mockImplementationOnce(() => delayedLoadMore.promise as never)
    .mockImplementationOnce(() => mutationRefresh.promise as never)
    .mockResolvedValueOnce(memberPageResult([], null));
  renderWithMessages(
    <OrganizationMemberDirectory
      currentActor={currentActor}
      initialPage={initialPage}
      organization={organization}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  fireEvent.click(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  );
  fireEvent.click(screen.getByRole("option", { name: "Administrator" }));

  await waitFor(() => expect(getMembers).toHaveBeenCalledTimes(2));
  expect(
    (getMembers.mock.calls[0]?.[0] as { signal?: AbortSignal }).signal?.aborted,
  ).toBe(true);
  await act(async () => {
    mutationRefresh.resolve(
      memberPageResult(
        [currentMember, { ...otherMember, role: "member" }],
        "cursor-from-refresh",
      ),
    );
  });
  expect(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  ).toHaveTextContent("Member");

  await act(async () => {
    delayedLoadMore.resolve(
      memberPageResult([staleMember], "cursor-from-stale-read"),
    );
  });
  expect(screen.queryByText("Stale Delayed User")).not.toBeInTheDocument();

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  await waitFor(() => {
    expect(getMembers).toHaveBeenNthCalledWith(3, {
      client: { id: "browser-client" },
      cache: "no-store",
      path: { organizationId: organization.id },
      query: { cursor: "cursor-from-refresh" },
      signal: expect.anything(),
    });
  });
});

it("keeps an added overlay until a later authoritative page contains it, then exposes later server changes", async () => {
  const confirmed = {
    ...otherMember,
    id: "01900000-0000-7000-8000-000000000034",
    userId: "01900000-0000-7000-8000-000000000024",
    name: "Confirmed Added User",
    email: "confirmed@example.com",
    role: "admin" as const,
    isOutsideAllowedEmailDomains: false,
  };
  const authoritative = {
    ...confirmed,
    name: "Authoritative Later User",
    email: "renamed@external.test",
    role: "owner" as const,
    isOutsideAllowedEmailDomains: true,
  };
  addMember.mockResolvedValue({ ok: true, data: confirmed });
  getMembers
    .mockResolvedValueOnce(
      memberPageResult([currentMember, otherMember], "cursor-authoritative"),
    )
    .mockResolvedValueOnce(memberPageResult([authoritative], null));
  renderWithMessages(
    <OrganizationMemberDirectory
      currentActor={currentActor}
      initialPage={{ ...initialPage, nextCursor: null }}
      organization={organization}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Add member" }));
  fireEvent.change(await screen.findByLabelText("User ID"), {
    target: { value: confirmed.userId },
  });
  fireEvent.click(screen.getByRole("combobox", { name: "Role" }));
  fireEvent.click(screen.getByRole("option", { name: "Administrator" }));
  fireEvent.click(screen.getByRole("button", { name: "Add" }));

  expect(await screen.findByText("Confirmed Added User")).toBeVisible();
  expect(
    screen.getByRole("combobox", { name: "Role for Confirmed Added User" }),
  ).toHaveTextContent("Administrator");

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));

  expect(await screen.findByText("Authoritative Later User")).toBeVisible();
  expect(screen.queryByText("Confirmed Added User")).not.toBeInTheDocument();
  expect(
    screen.getByRole("combobox", { name: "Role for Authoritative Later User" }),
  ).toHaveTextContent("Owner");
  expect(
    within(
      screen.getByRole("article", {
        name: "Authoritative Later User workspace member",
      }),
    ).getByText("Outside domain policy"),
  ).toBeVisible();
});

it("aborts and unlatches a never-resolving older recovery when a newer mutation recovery supersedes it", async () => {
  const neverResolvingRefresh =
    deferred<Awaited<ReturnType<typeof getOrganizationMembers>>>();
  const secondMember = {
    ...otherMember,
    id: "01900000-0000-7000-8000-000000000035",
    userId: "01900000-0000-7000-8000-000000000025",
    name: "Second User",
    email: "second@example.com",
    isOutsideAllowedEmailDomains: false,
  };
  const confirmedFirstRole = { ...otherMember, role: "admin" as const };
  const confirmedSecondRole = { ...secondMember, role: "admin" as const };
  updateRole
    .mockResolvedValueOnce({ ok: true, data: confirmedFirstRole })
    .mockResolvedValueOnce({ ok: true, data: confirmedSecondRole });
  getMembers
    .mockImplementationOnce(() => neverResolvingRefresh.promise as never)
    .mockResolvedValueOnce(
      memberPageResult(
        [currentMember, confirmedFirstRole, confirmedSecondRole],
        null,
      ),
    );
  renderWithMessages(
    <OrganizationMemberDirectory
      currentActor={currentActor}
      initialPage={{
        items: [currentMember, otherMember, secondMember],
        nextCursor: null,
      }}
      organization={organization}
    />,
  );

  fireEvent.click(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  );
  fireEvent.click(screen.getByRole("option", { name: "Administrator" }));
  await waitFor(() => expect(getMembers).toHaveBeenCalledTimes(1));
  expect(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  ).toBeDisabled();

  fireEvent.click(
    screen.getByRole("combobox", { name: "Role for Second User" }),
  );
  fireEvent.click(screen.getByRole("option", { name: "Administrator" }));

  await waitFor(() => expect(getMembers).toHaveBeenCalledTimes(2));
  await waitFor(() => {
    expect(
      screen.getByRole("combobox", { name: "Role for Other User" }),
    ).toBeEnabled();
  });
  expect(
    (getMembers.mock.calls[0]?.[0] as { signal?: AbortSignal }).signal?.aborted,
  ).toBe(true);
  expect(updateRole).toHaveBeenCalledTimes(2);
});
