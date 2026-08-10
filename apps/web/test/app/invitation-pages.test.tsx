import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { isValidElement, type ReactElement, type ReactNode } from "react";

import InviteSwitcherSlot from "@/src/app/(protected)/@applicationNavigation/invite/[invitationId]/page";
import AccountInvitationsSwitcherSlot from "@/src/app/(protected)/@applicationNavigation/user/invitations/page";
import SettingsInvitationsSwitcherSlot from "@/src/app/(protected)/@applicationNavigation/w/[organizationKey]/settings/invitations/page";
import InvitePage from "@/src/app/(protected)/invite/[invitationId]/page";
import AccountInvitationsPage from "@/src/app/(protected)/user/invitations/page";
import SettingsInvitationsPage from "@/src/app/(protected)/w/[organizationKey]/settings/invitations/page";
import { AccountInvitationList } from "@/src/features/collaboration/ui/account-invitation-list";
import { InvitationActivity } from "@/src/features/collaboration/ui/invitation-activity";
import { InvitationDecision } from "@/src/features/collaboration/ui/invitation-decision";
import { OrganizationFailure } from "@/src/features/organizations/ui/organization-list";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { loadServerAuthState } from "@/src/lib/api/auth/server/load-server-auth-state";
import { getAccountInvitations } from "@/src/lib/api/generated/sdk.gen";
import { loadAccountInvitations } from "@/src/lib/api/collaboration/server/load-account-invitations";
import { loadInvitationDecision } from "@/src/lib/api/collaboration/server/load-invitation-decision";
import { loadOrganizationInvitations } from "@/src/lib/api/collaboration/server/load-organization-invitations";
import { loadTeams } from "@/src/lib/api/collaboration/server/load-teams";
import type {
  InvitationDecisionResponse,
  InvitationResponse,
  OrganizationDetailResponse,
  TeamResponse,
} from "@/src/lib/api/generated/types.gen";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { withMessages } from "@/test/support/render";

jest.mock("next/server", () => ({ connection: jest.fn() }));
jest.mock("next/navigation", () => ({
  forbidden: jest.fn(() => {
    throw new Error("NEXT_FORBIDDEN");
  }),
  redirect: jest.fn((href: string) => {
    throw new Error(`NEXT_REDIRECT:${href}`);
  }),
  useRouter: () => ({ refresh: jest.fn(), replace: jest.fn() }),
}));
jest.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => (key: string) => {
    const messages: Record<string, string> = {
      "collaboration.invitations.account.title": "Invitations",
      "collaboration.invitations.account.description": "Invitation description",
      "collaboration.invitations.account.sectionTitle": "Pending invitations",
      "collaboration.invitations.settings.title": "Workspace invitations",
      "collaboration.invitations.settings.description":
        "Invitation description",
      "collaboration.invitations.settings.sectionTitle": "Invitation activity",
    };
    return messages[`${namespace}.${key}`] ?? key;
  },
}));
jest.mock("@/src/lib/api/auth/server/load-server-auth-state", () => ({
  loadServerAuthState: jest.fn(),
}));
jest.mock("@/src/features/authentication/load-protected-session", () => ({
  loadProtectedSession: jest.fn(),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/generated/sdk.gen", () => ({
  getAccountInvitations: jest.fn(),
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
jest.mock(
  "@/src/lib/api/collaboration/server/load-organization-invitations",
  () => ({ loadOrganizationInvitations: jest.fn() }),
);
jest.mock(
  "@/src/lib/api/collaboration/server/load-account-invitations",
  () => ({ loadAccountInvitations: jest.fn() }),
);
jest.mock(
  "@/src/lib/api/collaboration/server/load-invitation-decision",
  () => ({ loadInvitationDecision: jest.fn() }),
);
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

const invitation: InvitationResponse = {
  id: "01900000-0000-7000-8000-000000000101",
  organizationId: organization.id,
  organizationName: organization.name,
  canonicalOrganizationKey: organization.canonicalKey,
  teamId: null,
  teamName: null,
  email: "invitee@example.test",
  role: "member",
  status: "pending",
  displayState: "pending",
  expiresAt: "2026-08-03T12:00:00Z",
  createdAt: "2026-08-01T12:00:00Z",
  inviterId: "user-1",
  inviterName: "Owner",
  invitationPath: "/invite/01900000-0000-7000-8000-000000000101",
};

const decision: InvitationDecisionResponse = {
  invitation,
  state: "pending",
  canRespond: true,
};

const firstTeam: TeamResponse = {
  id: "01900000-0000-7000-8000-000000000201",
  organizationId: organization.id,
  name: "First-page team",
  memberCount: 0,
  membersIncluded: true,
  members: { items: [], nextCursor: null },
  createdAt: "2026-08-01T00:00:00Z",
  updatedAt: "2026-08-01T00:00:00Z",
};
const secondTeam: TeamResponse = {
  ...firstTeam,
  id: "01900000-0000-7000-8000-000000000202",
  name: "Second-page team",
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

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((next) => {
    resolve = next;
  });
  return { promise, resolve };
}

beforeEach(() => {
  jest.clearAllMocks();
  jest.mocked(loadProtectedSession).mockResolvedValue({
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
        id: "session-owner",
        createdAt: "2026-08-01T00:00:00Z",
        updatedAt: "2026-08-01T00:00:00Z",
        expiresAt: "2026-08-02T00:00:00Z",
        activeOrganizationId: organization.id,
      },
    },
  });
  jest
    .mocked(loadOrganization)
    .mockResolvedValue({ ok: true, data: organization });
  jest.mocked(loadOrganizationInvitations).mockResolvedValue({
    ok: true,
    data: { items: [invitation], nextCursor: "activity-next" },
  });
  jest.mocked(loadTeams).mockResolvedValue({
    ok: true,
    data: { items: [], nextCursor: null },
  });
  jest.mocked(loadAccountInvitations).mockResolvedValue({
    ok: true,
    data: { items: [invitation], nextCursor: null },
  });
  jest
    .mocked(loadInvitationDecision)
    .mockResolvedValue({ ok: true, data: decision });
  jest.mocked(loadServerAuthState).mockResolvedValue({
    ok: true,
    data: {
      capabilities: { localAutomationEnabled: false, providers: [] },
      session: {
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
    },
  });
});

it("forbids an ordinary member without loading or disclosing invitation activity", async () => {
  jest.mocked(loadOrganization).mockResolvedValue({
    ok: true,
    data: {
      ...organization,
      canonicalKey: "private-canonical-key",
      currentRole: "member",
      capabilities: {
        ...organization.capabilities,
        canManageInvitations: false,
        canManageApiKeys: false,
      },
    },
  });

  await expect(
    SettingsInvitationsPage({
      params: Promise.resolve({ organizationKey: "opaque-alias" }),
    }),
  ).rejects.toThrow("NEXT_FORBIDDEN");
  expect(loadOrganizationInvitations).not.toHaveBeenCalled();
  expect(loadTeams).not.toHaveBeenCalled();
  expect(jest.requireMock("next/navigation").redirect).not.toHaveBeenCalled();
});

it("canonicalizes an authorized settings route before loading activity", async () => {
  jest.mocked(loadOrganization).mockResolvedValue({
    ok: true,
    data: { ...organization, canonicalKey: "canonical-acme" },
  });
  await expect(
    SettingsInvitationsPage({
      params: Promise.resolve({ organizationKey: organization.id }),
    }),
  ).rejects.toThrow("NEXT_REDIRECT:/w/canonical-acme/settings/invitations");
  expect(loadOrganizationInvitations).not.toHaveBeenCalled();
});

it("loads authorized activity and team choices through the REST loaders", async () => {
  const page = await SettingsInvitationsPage({
    params: Promise.resolve({ organizationKey: organization.canonicalKey }),
  });
  const activity = findElementByType(page, InvitationActivity);

  expect(loadOrganizationInvitations).toHaveBeenCalledWith(organization.id, {
    limit: 20,
  });
  expect(loadTeams).toHaveBeenCalledWith(organization.id, { limit: 100 });
  expect(activity?.key).toBe(organization.id);
  expect((activity?.props as { organization: unknown }).organization).toEqual({
    id: organization.id,
    currentRole: "owner",
  });

  const view = render(withMessages(page));
  expect(
    Array.from(view.container.querySelectorAll("h1, h2"), (heading) =>
      heading.textContent?.trim(),
    ),
  ).toEqual(["Workspace invitations", "Invitation activity"]);
});

it("loads every team page and makes a later-page team selectable without duplicates", async () => {
  jest
    .mocked(loadTeams)
    .mockResolvedValueOnce({
      ok: true,
      data: { items: [firstTeam], nextCursor: "teams-page-2" },
    })
    .mockResolvedValueOnce({
      ok: true,
      data: {
        items: [{ ...firstTeam, name: "First-page team updated" }, secondTeam],
        nextCursor: null,
      },
    });

  render(
    withMessages(
      await SettingsInvitationsPage({
        params: Promise.resolve({ organizationKey: organization.canonicalKey }),
      }),
    ),
  );

  expect(loadTeams).toHaveBeenNthCalledWith(1, organization.id, { limit: 100 });
  expect(loadTeams).toHaveBeenNthCalledWith(2, organization.id, {
    cursor: "teams-page-2",
    limit: 100,
  });
  fireEvent.click(screen.getByRole("button", { name: "Create invitation" }));
  const dialog = screen.getByRole("dialog", {
    name: "Invite a workspace member",
  });
  fireEvent.click(within(dialog).getByRole("combobox", { name: "Team" }));
  expect(
    screen.getByRole("option", { name: "Second-page team" }),
  ).toBeVisible();
  expect(
    screen.getAllByRole("option", { name: "First-page team updated" }),
  ).toHaveLength(1);
  expect(
    screen.queryByRole("option", { name: "First-page team" }),
  ).not.toBeInTheDocument();
});

it("returns the stable page failure when a later team page cannot be loaded", async () => {
  const failure = {
    kind: "problem" as const,
    code: "team_permission_denied",
    status: 403,
    traceId: "trace-team-page",
  };
  jest
    .mocked(loadTeams)
    .mockResolvedValueOnce({
      ok: true,
      data: { items: [firstTeam], nextCursor: "teams-page-2" },
    })
    .mockResolvedValueOnce({ ok: false, failure });

  const page = await SettingsInvitationsPage({
    params: Promise.resolve({ organizationKey: organization.canonicalKey }),
  });
  const renderedFailure = findElementByType(page, OrganizationFailure);

  expect(renderedFailure).not.toBeNull();
  expect((renderedFailure!.props as { failure: unknown }).failure).toEqual(
    failure,
  );
  expect(loadTeams).toHaveBeenCalledTimes(2);
});

it("fails safely instead of following a repeated team cursor", async () => {
  jest
    .mocked(loadTeams)
    .mockResolvedValueOnce({
      ok: true,
      data: { items: [firstTeam], nextCursor: "repeated-team-cursor" },
    })
    .mockResolvedValueOnce({
      ok: true,
      data: { items: [secondTeam], nextCursor: "repeated-team-cursor" },
    });

  const page = await SettingsInvitationsPage({
    params: Promise.resolve({ organizationKey: organization.canonicalKey }),
  });
  const renderedFailure = findElementByType(page, OrganizationFailure);

  expect(renderedFailure).not.toBeNull();
  expect((renderedFailure!.props as { failure: unknown }).failure).toEqual({
    kind: "network",
    code: "api_unavailable",
  });
  expect(loadTeams).toHaveBeenCalledTimes(2);
});

it("uses only the account invitation loader and exposes the paged account list", async () => {
  const page = await AccountInvitationsPage();
  const list = findElementByType(page, AccountInvitationList);
  expect(loadAccountInvitations).toHaveBeenCalledWith({ limit: 20 });
  expect(loadOrganization).not.toHaveBeenCalled();
  expect((list?.props as { initialPage: unknown }).initialPage).toEqual({
    items: [invitation],
    nextCursor: null,
  });
});

it("uses distinct page and section headings for account invitations", async () => {
  const view = render(withMessages(await AccountInvitationsPage()));

  expect(
    Array.from(view.container.querySelectorAll("h1, h2"), (heading) =>
      heading.textContent?.trim(),
    ),
  ).toEqual(["Invitations", "Pending invitations"]);
});

it("renders the account empty state returned by the account-only endpoint", async () => {
  jest.mocked(loadAccountInvitations).mockResolvedValue({
    ok: true,
    data: { items: [], nextCursor: null },
  });
  render(withMessages(await AccountInvitationsPage()));
  expect(screen.getByText("No pending invitations")).toBeVisible();
  expect(
    screen.queryByRole("link", { name: "Review invitation" }),
  ).not.toBeInTheDocument();
});

it("pages account invitations through the generated account operation", async () => {
  jest.mocked(getAccountInvitations).mockResolvedValue({
    data: {
      data: {
        items: [
          {
            ...invitation,
            id: "invite-2",
            organizationName: "Second workspace",
          },
        ],
        nextCursor: null,
      },
    },
  } as Awaited<ReturnType<typeof getAccountInvitations>>);
  render(
    withMessages(
      <AccountInvitationList
        initialPage={{ items: [invitation], nextCursor: "account-next" }}
      />,
    ),
  );
  fireEvent.click(
    screen.getByRole("button", { name: "Load more invitations" }),
  );
  expect(await screen.findByText("Second workspace")).toBeVisible();
  expect(getAccountInvitations).toHaveBeenCalledWith(
    expect.objectContaining({
      query: { cursor: "account-next", limit: 20 },
    }),
  );
});

it("invalidates an outstanding account continuation when a new server page arrives", async () => {
  const olderContinuation =
    deferred<Awaited<ReturnType<typeof getAccountInvitations>>>();
  jest
    .mocked(getAccountInvitations)
    .mockReturnValueOnce(
      olderContinuation.promise as ReturnType<typeof getAccountInvitations>,
    )
    .mockResolvedValueOnce({
      data: {
        data: {
          items: [
            {
              ...invitation,
              id: "fresh-continuation",
              organizationName: "Fresh continuation",
            },
          ],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getAccountInvitations>>);
  const view = render(
    withMessages(
      <AccountInvitationList
        initialPage={{ items: [invitation], nextCursor: "old-cursor" }}
      />,
    ),
  );
  fireEvent.click(
    screen.getByRole("button", { name: "Load more invitations" }),
  );
  await waitFor(() => expect(getAccountInvitations).toHaveBeenCalledTimes(1));

  const freshInvitation = {
    ...invitation,
    id: "fresh-server-page",
    organizationName: "Fresh server page",
  };
  view.rerender(
    withMessages(
      <AccountInvitationList
        initialPage={{
          items: [freshInvitation],
          nextCursor: "fresh-cursor",
        }}
      />,
    ),
  );
  expect(screen.getByText("Fresh server page")).toBeVisible();
  expect(
    screen.queryByText(invitation.organizationName),
  ).not.toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "Load more invitations" }),
  ).toBeEnabled();

  await act(async () => {
    olderContinuation.resolve({
      data: {
        data: {
          items: [
            {
              ...invitation,
              id: "stale-continuation",
              organizationName: "Stale continuation",
            },
          ],
          nextCursor: "stale-cursor",
        },
      },
    } as Awaited<ReturnType<typeof getAccountInvitations>>);
    await olderContinuation.promise;
  });
  expect(screen.queryByText("Stale continuation")).not.toBeInTheDocument();
  expect(screen.getByText("Fresh server page")).toBeVisible();

  fireEvent.click(
    screen.getByRole("button", { name: "Load more invitations" }),
  );
  expect(await screen.findByText("Fresh continuation")).toBeVisible();
  expect(getAccountInvitations).toHaveBeenLastCalledWith(
    expect.objectContaining({ query: { cursor: "fresh-cursor", limit: 20 } }),
  );
});

it("loads a protected decision and passes verification and local API capability gates", async () => {
  jest.mocked(loadServerAuthState).mockResolvedValue({
    ok: true,
    data: {
      capabilities: { localAutomationEnabled: true, providers: [] },
      session: {
        authenticated: true,
        user: {
          id: "user-2",
          name: "Invitee",
          email: invitation.email,
          emailVerified: false,
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
    },
  });
  const page = await InvitePage({
    params: Promise.resolve({ invitationId: invitation.id }),
  });
  const component = findElementByType(page, InvitationDecision);
  expect(loadInvitationDecision).toHaveBeenCalledWith(invitation.id);
  expect(component?.key).toBe(invitation.id);
  expect(component?.props).toMatchObject({
    decision,
    emailVerified: false,
    localEmailConfirmationAvailable: true,
  });
});

it("maps an initial recipient-mismatch problem to the non-disclosing decision state", async () => {
  jest.mocked(loadInvitationDecision).mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "invitation_recipient_mismatch",
      status: 403,
      traceId: "trace-mismatch",
    },
  });

  const page = await InvitePage({
    params: Promise.resolve({ invitationId: invitation.id }),
  });
  const component = findElementByType(page, InvitationDecision);

  expect(component?.props).toMatchObject({
    decision: {
      invitation: null,
      state: "recipient-mismatch",
      canRespond: false,
    },
  });
  render(withMessages(page));
  expect(
    screen.getByText(
      "This invitation is not available for the current account.",
    ),
  ).toBeVisible();
  expect(
    screen.queryByText(invitation.organizationName),
  ).not.toBeInTheDocument();
  expect(screen.queryByText(invitation.email)).not.toBeInTheDocument();
  expect(screen.queryByText("trace-mismatch")).not.toBeInTheDocument();
});

it("redirects an anonymous invitation visitor back to the exact encoded route", async () => {
  jest.mocked(loadServerAuthState).mockResolvedValue({
    ok: true,
    data: {
      capabilities: { localAutomationEnabled: false, providers: [] },
      session: { authenticated: false, user: null, session: null },
    },
  });
  await expect(
    InvitePage({ params: Promise.resolve({ invitationId: "invite/id" }) }),
  ).rejects.toThrow(
    "NEXT_REDIRECT:/auth/login?redirect=%2Finvite%2Finvite%252Fid",
  );
  expect(loadInvitationDecision).not.toHaveBeenCalled();
});

it("provides exact application-navigation return paths for invitation routes", async () => {
  const accountSlot = AccountInvitationsSwitcherSlot();
  const inviteSlot = await InviteSwitcherSlot({
    params: Promise.resolve({ invitationId: "invite/id" }),
  });
  const settingsSlot = await SettingsInvitationsSwitcherSlot({
    params: Promise.resolve({ organizationKey: "acme" }),
  });

  expect(accountSlot.props).toEqual({ redirectPath: "/user/invitations" });
  expect(inviteSlot.props).toEqual({
    redirectPath: "/invite/invite%2Fid",
  });
  expect(settingsSlot.props).toEqual({
    redirectPath: "/w/acme/settings/invitations",
    organizationKey: "acme",
  });
});
