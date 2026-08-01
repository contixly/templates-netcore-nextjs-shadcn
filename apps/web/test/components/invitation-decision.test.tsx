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

it.each([
  ["accept", acceptInvitation, "Accept invitation"],
  ["reject", rejectInvitation, "Reject invitation"],
] as const)(
  "turns an already-member %s failure into a safe linked terminal state",
  async (_action, mutation, actionName) => {
    const staleServerProjection = {
      ...pending,
      invitation: { ...invitation },
    };
    mutation.mockResolvedValue({
      ok: false,
      failure: {
        kind: "problem",
        code: "invitation_recipient_already_member",
        status: 409,
      },
    });
    const view = renderWithMessages(
      <InvitationDecision
        decision={pending}
        emailVerified
        localEmailConfirmationAvailable={false}
      />,
    );
    const action = screen.getByRole("button", { name: actionName });

    fireEvent.click(action);
    expect(
      await screen.findByText("You already have access to this workspace."),
    ).toBeVisible();
    expect(screen.getByText(invitation.organizationName)).toBeVisible();
    expect(screen.getByText(invitation.email)).toBeVisible();
    expect(
      screen.getByRole("link", { name: "Open workspace" }),
    ).toHaveAttribute("href", "/w/acme-canonical/dashboard");
    expect(
      screen.queryByText(
        "This invitation is not available for the current account.",
      ),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText("The collaboration request could not be completed."),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText("invitation_recipient_already_member"),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Accept invitation" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Reject invitation" }),
    ).not.toBeInTheDocument();

    view.rerender(
      withMessages(
        <InvitationDecision
          decision={staleServerProjection}
          emailVerified
          localEmailConfirmationAvailable={false}
        />,
      ),
    );
    expect(
      screen.getByText("You already have access to this workspace."),
    ).toBeVisible();
    expect(
      screen.queryByRole("button", { name: "Accept invitation" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Reject invitation" }),
    ).not.toBeInTheDocument();
    fireEvent.click(action);
    expect(mutation).toHaveBeenCalledTimes(1);
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

it("purges a mounted invitation projection when account state changes to recipient mismatch", () => {
  const view = renderWithMessages(
    <InvitationDecision
      decision={pending}
      emailVerified
      localEmailConfirmationAvailable={false}
    />,
  );
  expect(screen.getByText(invitation.organizationName)).toBeVisible();

  view.rerender(
    withMessages(
      <InvitationDecision
        decision={{
          invitation: null,
          state: "recipient-mismatch",
          canRespond: false,
        }}
        emailVerified
        localEmailConfirmationAvailable={false}
      />,
    ),
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

it("invalidates an in-flight accept when mounted account state changes to mismatch", async () => {
  const mutation =
    deferred<Awaited<ReturnType<typeof acceptBrowserInvitation>>>();
  acceptInvitation.mockReturnValue(mutation.promise);
  const view = renderWithMessages(
    <InvitationDecision
      decision={pending}
      emailVerified
      localEmailConfirmationAvailable={false}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "Accept invitation" }));

  view.rerender(
    withMessages(
      <InvitationDecision
        decision={{
          invitation,
          state: "recipient-mismatch",
          canRespond: false,
        }}
        emailVerified
        localEmailConfirmationAvailable={false}
      />,
    ),
  );
  expect(
    screen.queryByText(invitation.organizationName),
  ).not.toBeInTheDocument();

  await act(async () => {
    mutation.resolve({
      ok: true,
      data: {
        invitationId: invitation.id,
        organizationId: invitation.organizationId,
        canonicalOrganizationKey: "must-not-navigate",
      },
    });
    await mutation.promise;
  });
  expect(replace).not.toHaveBeenCalled();
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
  const action = screen.getByRole("button", { name: "Accept invitation" });
  expect(screen.getByText("Acme")).toBeVisible();
  expect(screen.getByText("Platform")).toBeVisible();
  fireEvent.click(action);

  await waitFor(() =>
    expect(replace).toHaveBeenCalledWith("/w/canonical%20key/dashboard"),
  );
  expect(screen.getByText("This invitation has been accepted.")).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "Accept invitation" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Reject invitation" }),
  ).not.toBeInTheDocument();
  expect(acceptInvitation).toHaveBeenCalledWith(
    { id: "browser-client" },
    invitation.id,
  );
  expect(replace).toHaveBeenCalledTimes(1);
  fireEvent.click(action);
  expect(acceptInvitation).toHaveBeenCalledTimes(1);
});

it("keeps accepted state terminal when client navigation throws", async () => {
  const staleServerProjection = {
    ...pending,
    invitation: { ...invitation },
  };
  acceptInvitation.mockResolvedValue({
    ok: true,
    data: {
      invitationId: invitation.id,
      organizationId: invitation.organizationId,
      canonicalOrganizationKey: "acme",
    },
  });
  replace.mockImplementationOnce(() => {
    throw new Error("private navigation failure");
  });
  const view = renderWithMessages(
    <InvitationDecision
      decision={pending}
      emailVerified
      localEmailConfirmationAvailable={false}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Accept invitation" }));
  expect(
    await screen.findByText("This invitation has been accepted."),
  ).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "Accept invitation" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByText("private navigation failure"),
  ).not.toBeInTheDocument();

  view.rerender(
    withMessages(
      <InvitationDecision
        decision={staleServerProjection}
        emailVerified
        localEmailConfirmationAvailable={false}
      />,
    ),
  );
  expect(screen.getByText("This invitation has been accepted.")).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "Accept invitation" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Reject invitation" }),
  ).not.toBeInTheDocument();
  expect(acceptInvitation).toHaveBeenCalledTimes(1);
});

it("keeps accepted state over delayed stale RSC projections and adopts a terminal acknowledgement", async () => {
  const firstStaleServerProjection = {
    ...pending,
    invitation: { ...invitation },
  };
  const secondStaleServerProjection = {
    ...pending,
    invitation: { ...invitation },
  };
  acceptInvitation.mockResolvedValue({
    ok: true,
    data: {
      invitationId: invitation.id,
      organizationId: invitation.organizationId,
      canonicalOrganizationKey: "acme",
    },
  });
  const view = renderWithMessages(
    <InvitationDecision
      decision={pending}
      emailVerified
      localEmailConfirmationAvailable={false}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Accept invitation" }));
  expect(
    await screen.findByText("This invitation has been accepted."),
  ).toBeVisible();
  expect(replace).toHaveBeenCalledWith("/w/acme/dashboard");

  view.rerender(
    withMessages(
      <InvitationDecision
        decision={firstStaleServerProjection}
        emailVerified
        localEmailConfirmationAvailable={false}
      />,
    ),
  );
  expect(screen.getByText("This invitation has been accepted.")).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "Accept invitation" }),
  ).not.toBeInTheDocument();

  view.rerender(
    withMessages(
      <InvitationDecision
        decision={{
          invitation: {
            ...invitation,
            organizationName: "Authoritative Acme",
            status: "accepted",
            displayState: "accepted",
          },
          state: "accepted",
          canRespond: false,
        }}
        emailVerified
        localEmailConfirmationAvailable={false}
      />,
    ),
  );
  expect(screen.getByText("Authoritative Acme")).toBeVisible();

  view.rerender(
    withMessages(
      <InvitationDecision
        decision={secondStaleServerProjection}
        emailVerified
        localEmailConfirmationAvailable={false}
      />,
    ),
  );
  expect(screen.getByText("This invitation has been accepted.")).toBeVisible();
  expect(screen.getByText("Authoritative Acme")).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "Accept invitation" }),
  ).not.toBeInTheDocument();
});

it.each([
  ["invitation_expired", "This invitation has expired."],
  [
    "invitation_domain_restricted",
    "The workspace email policy no longer allows this invitation.",
  ],
  [
    "invitation_email_verification_required",
    "Verify the invited email address before responding.",
  ],
] as const)(
  "turns the terminal %s mutation failure into a non-replayable read-only state",
  async (code, message) => {
    acceptInvitation.mockResolvedValue({
      ok: false,
      failure: { kind: "problem", code, status: 409 },
    });
    renderWithMessages(
      <InvitationDecision
        decision={pending}
        emailVerified
        localEmailConfirmationAvailable={false}
      />,
    );
    const action = screen.getByRole("button", { name: "Accept invitation" });

    fireEvent.click(action);
    expect(await screen.findByText(message)).toBeVisible();
    expect(
      screen.queryByRole("button", { name: "Accept invitation" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Reject invitation" }),
    ).not.toBeInTheDocument();
    fireEvent.click(action);
    expect(acceptInvitation).toHaveBeenCalledTimes(1);
  },
);

it("reconciles invitation_not_pending once and remains non-actionable when that read fails", async () => {
  rejectInvitation.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "invitation_not_pending",
      status: 409,
    },
  });
  loadDecision.mockResolvedValue({
    error: { detail: "private reconciliation failure" },
    response: { status: 503 } as Response,
  } as Awaited<ReturnType<typeof getInvitationDecision>>);
  renderWithMessages(
    <InvitationDecision
      decision={pending}
      emailVerified
      localEmailConfirmationAvailable={false}
    />,
  );
  const action = screen.getByRole("button", { name: "Reject invitation" });

  fireEvent.click(action);
  expect(
    await screen.findByText("This invitation has already been resolved."),
  ).toBeVisible();
  expect(
    await screen.findByText(
      "The latest invitation state could not be loaded. Response actions remain disabled.",
    ),
  ).toBeVisible();
  expect(
    screen.queryByText("private reconciliation failure"),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Reject invitation" }),
  ).not.toBeInTheDocument();
  fireEvent.click(action);
  expect(rejectInvitation).toHaveBeenCalledTimes(1);
  expect(loadDecision).toHaveBeenCalledTimes(1);

  fireEvent.click(screen.getByRole("button", { name: "Retry" }));
  await waitFor(() => expect(loadDecision).toHaveBeenCalledTimes(2));
  expect(rejectInvitation).toHaveBeenCalledTimes(1);
});

it("does not trust an actionable pending read after invitation_not_pending", async () => {
  acceptInvitation.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "invitation_not_pending",
      status: 409,
    },
  });
  loadDecision.mockResolvedValue({
    data: { data: pending },
  } as Awaited<ReturnType<typeof getInvitationDecision>>);
  renderWithMessages(
    <InvitationDecision
      decision={pending}
      emailVerified
      localEmailConfirmationAvailable={false}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Accept invitation" }));
  await waitFor(() => expect(loadDecision).toHaveBeenCalledTimes(1));
  expect(
    screen.queryByRole("button", { name: "Accept invitation" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Reject invitation" }),
  ).not.toBeInTheDocument();
  expect(acceptInvitation).toHaveBeenCalledTimes(1);
  expect(
    screen.getByText(
      "The latest invitation state could not be loaded. Response actions remain disabled.",
    ),
  ).toBeVisible();
});

it("purges details immediately when a mutation reports recipient mismatch", async () => {
  rejectInvitation.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "invitation_recipient_mismatch",
      status: 403,
    },
  });
  renderWithMessages(
    <InvitationDecision
      decision={pending}
      emailVerified
      localEmailConfirmationAvailable={false}
    />,
  );
  const action = screen.getByRole("button", { name: "Reject invitation" });

  fireEvent.click(action);
  expect(
    await screen.findByText(
      "This invitation is not available for the current account.",
    ),
  ).toBeVisible();
  expect(
    screen.queryByText(invitation.organizationName),
  ).not.toBeInTheDocument();
  expect(screen.queryByText(invitation.email)).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Reject invitation" }),
  ).not.toBeInTheDocument();
  fireEvent.click(action);
  expect(rejectInvitation).toHaveBeenCalledTimes(1);
});

it("purges details when a post-rejection refresh reports recipient mismatch", async () => {
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
    error: { code: "invitation_recipient_mismatch" },
    response: { status: 403 } as Response,
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
    await screen.findByText(
      "This invitation is not available for the current account.",
    ),
  ).toBeVisible();
  expect(
    screen.queryByText(invitation.organizationName),
  ).not.toBeInTheDocument();
  expect(screen.queryByText(invitation.email)).not.toBeInTheDocument();
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
  const staleServerProjection = {
    ...pending,
    invitation: { ...invitation },
  };
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
  expect(
    await screen.findByText(
      "Your response was saved, but the invitation could not be refreshed.",
    ),
  ).toBeVisible();
  expect(screen.queryByText("private refresh detail")).not.toBeInTheDocument();
  expect(rejectInvitation).toHaveBeenCalledTimes(1);

  view.rerender(
    withMessages(
      <InvitationDecision
        decision={staleServerProjection}
        emailVerified
        localEmailConfirmationAvailable={false}
      />,
    ),
  );
  expect(screen.getByText("This invitation has been rejected.")).toBeVisible();
  expect(
    screen.getByText(
      "Your response was saved, but the invitation could not be refreshed.",
    ),
  ).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "Accept invitation" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Reject invitation" }),
  ).not.toBeInTheDocument();

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
  const secondInvitation = {
    ...invitation,
    id: "invite-2",
    email: "two@example.test",
  };
  view.rerender(
    withMessages(
      <Activity mode="visible">
        <InvitationDecision
          decision={{ ...pending, invitation: secondInvitation }}
          emailVerified
          localEmailConfirmationAvailable={false}
        />
      </Activity>,
    ),
  );
  fireEvent.click(screen.getByRole("button", { name: "Accept invitation" }));
  const replacementInvitation = {
    ...invitation,
    id: "invite-3",
    email: "three@example.test",
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
        invitationId: secondInvitation.id,
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
