import { fireEvent, screen, waitFor, within } from "@testing-library/react";

import { InvitationCreateDialog } from "@/src/components/collaboration/invitation-create-dialog";
import { createBrowserInvitation } from "@/src/lib/api/collaboration/browser/collaboration-mutations";
import type { InvitationResponse } from "@/src/lib/api/generated/types.gen";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock(
  "@/src/lib/api/collaboration/browser/collaboration-mutations",
  () => ({ createBrowserInvitation: jest.fn() }),
);

const createdInvitation: InvitationResponse = {
  id: "01900000-0000-7000-8000-000000000101",
  organizationId: "01900000-0000-7000-8000-000000000001",
  organizationName: "Acme",
  canonicalOrganizationKey: "acme",
  teamId: null,
  teamName: null,
  email: "invitee@example.test",
  role: "member",
  status: "pending",
  displayState: "pending",
  expiresAt: "2026-08-03T12:00:00Z",
  createdAt: "2026-08-01T12:00:00Z",
  inviterId: "01900000-0000-7000-8000-000000000002",
  inviterName: "Owner",
  invitationPath: "/invite/01900000-0000-7000-8000-000000000101",
};

const createInvitation = jest.mocked(createBrowserInvitation);

function chooseOption(dialog: HTMLElement, label: string, option: string) {
  fireEvent.click(within(dialog).getByRole("combobox", { name: label }));
  fireEvent.click(screen.getByRole("option", { name: option }));
}

beforeEach(() => {
  jest.clearAllMocks();
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    value: { writeText: jest.fn().mockResolvedValue(undefined) },
  });
});

it("normalizes a workspace-only invitation and limits administrators to assignable roles", async () => {
  createInvitation.mockResolvedValue({
    ok: true,
    data: createdInvitation,
  });
  const confirmed = jest.fn();
  renderWithMessages(
    <InvitationCreateDialog
      currentRole="admin"
      onConfirmed={confirmed}
      organizationId={createdInvitation.organizationId}
      teams={[{ id: "team-1", name: "Platform" }]}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Create invitation" }));
  const dialog = screen.getByRole("dialog", {
    name: "Invite a workspace member",
  });
  fireEvent.change(within(dialog).getByLabelText("Email address"), {
    target: { value: "  INVITEE@Example.Test  " },
  });
  chooseOption(dialog, "Workspace role", "Administrator");
  expect(
    screen.queryByRole("option", { name: "Owner" }),
  ).not.toBeInTheDocument();
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Create invitation" }),
  );

  await waitFor(() =>
    expect(createInvitation).toHaveBeenCalledWith(
      { id: "browser-client" },
      createdInvitation.organizationId,
      { email: "invitee@example.test", role: "admin", teamId: null },
    ),
  );
  expect(confirmed).toHaveBeenCalledWith(createdInvitation);
});

it("validates email before mutation and submits an owner-only team target", async () => {
  createInvitation.mockResolvedValue({
    ok: true,
    data: {
      ...createdInvitation,
      teamId: "team-1",
      teamName: "Platform",
      role: "owner",
    },
  });
  renderWithMessages(
    <InvitationCreateDialog
      currentRole="owner"
      onConfirmed={jest.fn()}
      organizationId={createdInvitation.organizationId}
      teams={[{ id: "team-1", name: "Platform" }]}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Create invitation" }));
  const dialog = screen.getByRole("dialog", {
    name: "Invite a workspace member",
  });
  const email = within(dialog).getByLabelText("Email address");
  fireEvent.change(email, { target: { value: "not-an-email" } });
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Create invitation" }),
  );
  expect(
    await within(dialog).findByText("Enter a valid email address."),
  ).toBeVisible();
  expect(email).toHaveAttribute("aria-invalid", "true");
  expect(createInvitation).not.toHaveBeenCalled();

  fireEvent.change(email, { target: { value: "owner@example.test" } });
  chooseOption(dialog, "Workspace role", "Owner");
  chooseOption(dialog, "Team", "Platform");
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Create invitation" }),
  );

  await waitFor(() =>
    expect(createInvitation).toHaveBeenCalledWith(
      { id: "browser-client" },
      createdInvitation.organizationId,
      { email: "owner@example.test", role: "owner", teamId: "team-1" },
    ),
  );
});

it("shows only a validated same-origin returned link and reports copy failure safely", async () => {
  createInvitation.mockResolvedValue({ ok: true, data: createdInvitation });
  const writeText = jest
    .fn()
    .mockRejectedValueOnce(new Error("private clipboard failure"))
    .mockResolvedValueOnce(undefined);
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    value: { writeText },
  });
  renderWithMessages(
    <InvitationCreateDialog
      currentRole="owner"
      onConfirmed={jest.fn()}
      organizationId={createdInvitation.organizationId}
      teams={[]}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Create invitation" }));
  const dialog = screen.getByRole("dialog", {
    name: "Invite a workspace member",
  });
  fireEvent.change(within(dialog).getByLabelText("Email address"), {
    target: { value: createdInvitation.email },
  });
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Create invitation" }),
  );

  const link = await within(dialog).findByLabelText("Invitation link");
  expect(link).toHaveValue(
    new URL(createdInvitation.invitationPath, window.location.origin).href,
  );
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Copy invitation link" }),
  );
  expect(
    await within(dialog).findByText(
      "The invitation link could not be copied. Copy it manually.",
    ),
  ).toBeVisible();
  expect(
    screen.queryByText("private clipboard failure"),
  ).not.toBeInTheDocument();
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Copy invitation link" }),
  );
  expect(
    await within(dialog).findByText("Invitation link copied."),
  ).toBeVisible();
});

it("does not expose or copy an unexpected cross-origin invitation path", async () => {
  createInvitation.mockResolvedValue({
    ok: true,
    data: {
      ...createdInvitation,
      invitationPath: "//attacker.example/invite/x",
    },
  });
  renderWithMessages(
    <InvitationCreateDialog
      currentRole="owner"
      onConfirmed={jest.fn()}
      organizationId={createdInvitation.organizationId}
      teams={[]}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Create invitation" }));
  const dialog = screen.getByRole("dialog", {
    name: "Invite a workspace member",
  });
  fireEvent.change(within(dialog).getByLabelText("Email address"), {
    target: { value: createdInvitation.email },
  });
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Create invitation" }),
  );

  expect(
    await within(dialog).findByText(
      "The collaboration request could not be completed.",
    ),
  ).toBeVisible();
  expect(
    within(dialog).queryByLabelText("Invitation link"),
  ).not.toBeInTheDocument();
  expect(navigator.clipboard.writeText).not.toHaveBeenCalled();
});
