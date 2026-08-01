import { fireEvent, screen, waitFor } from "@testing-library/react";

import { InvitationActivity } from "@/src/components/collaboration/invitation-activity";
import { getOrganizationInvitations } from "@/src/lib/api/generated/sdk.gen";
import type { InvitationResponse } from "@/src/lib/api/generated/types.gen";
import { renderWithMessages } from "@/test/support/render";

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
