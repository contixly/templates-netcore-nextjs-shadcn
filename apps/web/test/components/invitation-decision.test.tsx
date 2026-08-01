import { act, fireEvent, screen, waitFor } from "@testing-library/react";
import { Activity } from "react";

import { InvitationDecision } from "@/src/components/collaboration/invitation-decision";
import { confirmLocalAutomationEmail } from "@/src/lib/api/auth/browser/confirm-local-automation-email";
import {
  acceptBrowserInvitation,
  rejectBrowserInvitation,
} from "@/src/lib/api/collaboration/browser/collaboration-mutations";
import { getInvitationDecision } from "@/src/lib/api/generated/sdk.gen";
import type {
  InvitationDecisionResponse,
  InvitationResponse,
} from "@/src/lib/api/generated/types.gen";
import { renderWithMessages, withMessages } from "@/test/support/render";

const replace = jest.fn();
const refresh = jest.fn();
jest.mock("next/navigation", () => ({
  useRouter: () => ({ replace, refresh }),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/generated/sdk.gen", () => ({
  getInvitationDecision: jest.fn(),
}));
jest.mock(
  "@/src/lib/api/collaboration/browser/collaboration-mutations",
  () => ({
    acceptBrowserInvitation: jest.fn(),
    rejectBrowserInvitation: jest.fn(),
  }),
);
jest.mock("@/src/lib/api/auth/browser/confirm-local-automation-email", () => ({
  confirmLocalAutomationEmail: jest.fn(),
}));

const invitation: InvitationResponse = {
  id: "01900000-0000-7000-8000-000000000101",
  organizationId: "01900000-0000-7000-8000-000000000001",
  organizationName: "Acme",
  canonicalOrganizationKey: "acme-canonical",
  teamId: "team-1",
  teamName: "Platform",
  email: "invitee@example.test",
  role: "admin",
  status: "pending",
  displayState: "pending",
  expiresAt: "2026-08-03T12:00:00Z",
  createdAt: "2026-08-01T12:00:00Z",
  inviterId: "user-1",
  inviterName: "Owner",
  invitationPath: "/invite/01900000-0000-7000-8000-000000000101",
};

const pending: InvitationDecisionResponse = {
  invitation,
  state: "pending",
  canRespond: true,
};

const acceptInvitation = jest.mocked(acceptBrowserInvitation);
const rejectInvitation = jest.mocked(rejectBrowserInvitation);
const loadDecision = jest.mocked(getInvitationDecision);
const confirmEmail = jest.mocked(confirmLocalAutomationEmail);

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((next) => {
    resolve = next;
  });
  return { promise, resolve };
}

beforeEach(() => {
  jest.clearAllMocks();
});

it.each([
  ["accepted", "This invitation has been accepted."],
  ["rejected", "This invitation has been rejected."],
  ["canceled", "This invitation was canceled."],
  ["expired", "This invitation has expired."],
  [
    "domain-restricted",
    "The workspace email policy no longer allows this invitation.",
  ],
  ["already-member", "You already have access to this workspace."],
] as const)(
  "renders the %s state without decision actions",
  (state, message) => {
    renderWithMessages(
      <InvitationDecision
        decision={{ ...pending, state, canRespond: false }}
        emailVerified
        localEmailConfirmationAvailable={false}
      />,
    );

    expect(screen.getByText(message)).toBeVisible();
    expect(
      screen.queryByRole("button", { name: "Accept invitation" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Reject invitation" }),
    ).not.toBeInTheDocument();
  },
);

it("never discloses invitation details for a recipient mismatch", () => {
  renderWithMessages(
    <InvitationDecision
      decision={{ ...pending, state: "recipient-mismatch", canRespond: false }}
      emailVerified
      localEmailConfirmationAvailable={false}
    />,
  );

  expect(
    screen.getByText(
      "This invitation is not available for the current account.",
    ),
  ).toBeVisible();
  for (const secret of [
    invitation.organizationName,
    invitation.email,
    invitation.inviterName,
    invitation.teamName!,
  ]) {
    expect(screen.queryByText(secret)).not.toBeInTheDocument();
  }
});

it("shows local email confirmation only when the API capability and state allow it", () => {
  const decision = {
    ...pending,
    state: "email-verification-required" as const,
    canRespond: false,
  };
  const view = renderWithMessages(
    <InvitationDecision
      decision={decision}
      emailVerified={false}
      localEmailConfirmationAvailable={false}
    />,
  );
  expect(
    screen.queryByRole("button", { name: "Confirm email for local testing" }),
  ).not.toBeInTheDocument();

  view.rerender(
    withMessages(
      <InvitationDecision
        decision={decision}
        emailVerified={false}
        localEmailConfirmationAvailable
      />,
    ),
  );
  expect(
    screen.getByRole("button", { name: "Confirm email for local testing" }),
  ).toBeVisible();
  expect(
    screen.getByText(/development and test environments only/i),
  ).toBeVisible();
});

it("uses the generated local-only confirmation adapter and reloads eligibility", async () => {
  confirmEmail.mockResolvedValue({
    ok: true,
    data: {
      authenticated: true,
      user: {
        id: "user-2",
        name: "Invitee",
        email: invitation.email,
        emailVerified: true,
        image: null,
      },
      session: {
        id: "session-1",
        createdAt: "2026-08-01T00:00:00Z",
        updatedAt: "2026-08-01T00:00:00Z",
        expiresAt: "2026-08-02T00:00:00Z",
        activeOrganizationId: null,
      },
    },
  });
  loadDecision.mockResolvedValue({
    data: { data: pending },
  } as Awaited<ReturnType<typeof getInvitationDecision>>);
  renderWithMessages(
    <InvitationDecision
      decision={{
        ...pending,
        state: "email-verification-required",
        canRespond: false,
      }}
      emailVerified={false}
      localEmailConfirmationAvailable
    />,
  );
  fireEvent.click(
    screen.getByRole("button", { name: "Confirm email for local testing" }),
  );
  expect(
    await screen.findByRole("button", { name: "Accept invitation" }),
  ).toBeVisible();
  expect(confirmEmail).toHaveBeenCalledWith({ id: "browser-client" });
  expect(loadDecision).toHaveBeenCalledWith(
    expect.objectContaining({ path: { invitationId: invitation.id } }),
  );
});

it("renders stable failure copy and trace without exposing a raw problem code", async () => {
  acceptInvitation.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "invitation_expired",
      status: 409,
      traceId: "trace-invitation",
    },
  });
  renderWithMessages(
    <InvitationDecision
      decision={pending}
      emailVerified
      localEmailConfirmationAvailable={false}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "Accept invitation" }));
  expect(await screen.findByText("This invitation has expired.")).toBeVisible();
  expect(screen.getByRole("alert")).toHaveTextContent("trace-invitation");
  expect(screen.queryByText("invitation_expired")).not.toBeInTheDocument();
});

it("allows only a verified actionable pending invitation to accept and uses the returned canonical dashboard", async () => {
  acceptInvitation.mockResolvedValue({
    ok: true,
    data: {
      invitationId: invitation.id,
      organizationId: invitation.organizationId,
      canonicalOrganizationKey: "canonical key",
    },
  });
  renderWithMessages(
    <InvitationDecision
      decision={pending}
      emailVerified
      localEmailConfirmationAvailable={false}
    />,
  );
  expect(screen.getByText("Acme")).toBeVisible();
  expect(screen.getByText("Platform")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "Accept invitation" }));

  await waitFor(() =>
    expect(replace).toHaveBeenCalledWith("/w/canonical%20key/dashboard"),
  );
  expect(acceptInvitation).toHaveBeenCalledWith(
    { id: "browser-client" },
    invitation.id,
  );
  expect(replace).toHaveBeenCalledTimes(1);
});

it("does not render response actions when the account is not verified even if canRespond is inconsistent", () => {
  renderWithMessages(
    <InvitationDecision
      decision={pending}
      emailVerified={false}
      localEmailConfirmationAvailable={false}
    />,
  );
  expect(
    screen.queryByRole("button", { name: "Accept invitation" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Reject invitation" }),
  ).not.toBeInTheDocument();
});

it("commits rejection locally and reports a failed GET refresh without repeating POST", async () => {
  rejectInvitation.mockResolvedValue({
    ok: true,
    data: {
      invitation: {
        ...invitation,
        status: "rejected",
        displayState: "rejected",
      },
      state: "rejected",
      canRespond: false,
    },
  });
  loadDecision.mockResolvedValue({
    error: { detail: "private refresh detail" },
    response: { status: 503 } as Response,
  } as Awaited<ReturnType<typeof getInvitationDecision>>);
  renderWithMessages(
    <InvitationDecision
      decision={pending}
      emailVerified
      localEmailConfirmationAvailable={false}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Reject invitation" }));
  expect(
    await screen.findByText("This invitation has been rejected."),
  ).toBeVisible();
  expect(
    await screen.findByText(
      "Your response was saved, but the invitation could not be refreshed.",
    ),
  ).toBeVisible();
  expect(screen.queryByText("private refresh detail")).not.toBeInTheDocument();
  expect(rejectInvitation).toHaveBeenCalledTimes(1);
  fireEvent.click(screen.getByRole("button", { name: "Retry" }));
  await waitFor(() => expect(loadDecision).toHaveBeenCalledTimes(2));
  expect(rejectInvitation).toHaveBeenCalledTimes(1);
});

it("defers one accept navigation while Activity-hidden and discards it after a different invitation replaces the instance", async () => {
  const mutation =
    deferred<Awaited<ReturnType<typeof acceptBrowserInvitation>>>();
  acceptInvitation.mockReturnValue(mutation.promise);
  const first = (
    <InvitationDecision
      decision={pending}
      emailVerified
      localEmailConfirmationAvailable={false}
    />
  );
  const view = renderWithMessages(<Activity mode="visible">{first}</Activity>);
  fireEvent.click(screen.getByRole("button", { name: "Accept invitation" }));
  view.rerender(withMessages(<Activity mode="hidden">{first}</Activity>));
  await act(async () => {
    mutation.resolve({
      ok: true,
      data: {
        invitationId: invitation.id,
        organizationId: invitation.organizationId,
        canonicalOrganizationKey: "acme",
      },
    });
    await mutation.promise;
  });
  expect(replace).not.toHaveBeenCalled();

  view.rerender(withMessages(<Activity mode="visible">{first}</Activity>));
  expect(replace).toHaveBeenCalledWith("/w/acme/dashboard");
  expect(replace).toHaveBeenCalledTimes(1);
  view.rerender(withMessages(<Activity mode="hidden">{first}</Activity>));
  view.rerender(withMessages(<Activity mode="visible">{first}</Activity>));
  expect(replace).toHaveBeenCalledTimes(1);

  const stale = deferred<Awaited<ReturnType<typeof acceptBrowserInvitation>>>();
  acceptInvitation.mockReturnValue(stale.promise);
  fireEvent.click(screen.getByRole("button", { name: "Accept invitation" }));
  const replacementInvitation = {
    ...invitation,
    id: "invite-2",
    email: "two@example.test",
  };
  view.rerender(
    withMessages(
      <Activity mode="visible">
        <InvitationDecision
          decision={{ ...pending, invitation: replacementInvitation }}
          emailVerified
          localEmailConfirmationAvailable={false}
        />
      </Activity>,
    ),
  );
  await act(async () => {
    stale.resolve({
      ok: true,
      data: {
        invitationId: invitation.id,
        organizationId: invitation.organizationId,
        canonicalOrganizationKey: "stale",
      },
    });
    await stale.promise;
  });
  expect(replace).toHaveBeenCalledTimes(1);
});

it("does not let a rejection refresh overwrite a different invitation instance", async () => {
  const refreshRead =
    deferred<Awaited<ReturnType<typeof getInvitationDecision>>>();
  rejectInvitation.mockResolvedValue({
    ok: true,
    data: {
      invitation: {
        ...invitation,
        status: "rejected",
        displayState: "rejected",
      },
      state: "rejected",
      canRespond: false,
    },
  });
  loadDecision.mockReturnValue(
    refreshRead.promise as ReturnType<typeof getInvitationDecision>,
  );
  const view = renderWithMessages(
    <InvitationDecision
      decision={pending}
      emailVerified
      localEmailConfirmationAvailable={false}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "Reject invitation" }));
  expect(
    await screen.findByText("This invitation has been rejected."),
  ).toBeVisible();

  const replacement = {
    ...pending,
    invitation: { ...invitation, id: "invite-2", organizationName: "Other" },
  };
  view.rerender(
    withMessages(
      <InvitationDecision
        decision={replacement}
        emailVerified
        localEmailConfirmationAvailable={false}
      />,
    ),
  );
  await act(async () => {
    refreshRead.resolve({
      data: {
        data: {
          invitation: { ...invitation, organizationName: "Stale Acme" },
          state: "rejected",
          canRespond: false,
        },
      },
    } as Awaited<ReturnType<typeof getInvitationDecision>>);
    await refreshRead.promise;
  });

  expect(screen.getByText("Other")).toBeVisible();
  expect(screen.queryByText("Stale Acme")).not.toBeInTheDocument();
});
