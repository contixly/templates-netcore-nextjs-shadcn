import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import {
  Activity,
  isValidElement,
  Suspense,
  useLayoutEffect,
  type ReactElement,
  type ReactNode,
} from "react";
import { createRoot } from "react-dom/client";

import SettingsSwitcherSlot from "@/src/app/(site)/@organizationSwitcher/w/[organizationKey]/settings/page";
import RolesSwitcherSlot from "@/src/app/(site)/@organizationSwitcher/w/[organizationKey]/settings/roles/page";
import UsersSwitcherSlot from "@/src/app/(site)/@organizationSwitcher/w/[organizationKey]/settings/users/page";
import WorkspaceSwitcherSlot from "@/src/app/(site)/@organizationSwitcher/w/[organizationKey]/settings/workspace/page";
import SettingsLayout, {
  AuthenticatedOrganizationSettingsShell,
} from "@/src/app/(site)/w/[organizationKey]/settings/layout";
import SettingsPage from "@/src/app/(site)/w/[organizationKey]/settings/page";
import RolesPage from "@/src/app/(site)/w/[organizationKey]/settings/roles/page";
import UsersPage from "@/src/app/(site)/w/[organizationKey]/settings/users/page";
import WorkspacePage from "@/src/app/(site)/w/[organizationKey]/settings/workspace/page";
import { OrganizationDeleteDialog } from "@/src/components/organizations/organization-delete-dialog";
import { OrganizationMemberDirectory } from "@/src/components/organizations/organization-member-directory";
import { OrganizationSettingsForm } from "@/src/components/organizations/organization-settings-form";
import { OrganizationSettingsNav } from "@/src/components/organizations/organization-settings-nav";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import { getOrganizationMembers } from "@/src/lib/api/generated/sdk.gen";
import type {
  OrganizationDetailResponse,
  OrganizationMemberPageResponse,
  OrganizationMemberResponse,
  OrganizationSummaryResponse,
} from "@/src/lib/api/generated/types.gen";
import { loadOrganizationMembers } from "@/src/lib/api/organizations/server/load-organization-members";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";
import {
  addBrowserOrganizationMember,
  deleteBrowserOrganization,
  updateBrowserOrganization,
  updateBrowserOrganizationMemberRole,
} from "@/src/lib/api/organizations/browser/organization-mutations";
import { renderWithMessages, withMessages } from "@/test/support/render";

const pathname = jest.fn(() => "/w/acme/settings/workspace");
const replace = jest.fn();
const refresh = jest.fn();

jest.mock("next/server", () => ({
  connection: jest.fn().mockResolvedValue(undefined),
}));
jest.mock("next/navigation", () => ({
  forbidden: jest.fn(() => {
    throw new Error("NEXT_FORBIDDEN");
  }),
  redirect: jest.fn((href: string) => {
    throw new Error(`NEXT_REDIRECT:${href}`);
  }),
  usePathname: () => pathname(),
  useRouter: () => ({ replace, refresh }),
}));
jest.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => {
    const values: Record<string, string> = {
      "organizations.settings.navigation.loading": "Loading settings",
      "organizations.settings.pages.workspace.title": "Workspace settings",
      "organizations.settings.pages.workspace.description":
        "Manage workspace identity and domain policy.",
      "organizations.settings.pages.users.title": "Workspace users",
      "organizations.settings.pages.users.description":
        "Review members and built-in roles.",
      "organizations.settings.pages.roles.title": "Workspace roles",
      "organizations.settings.pages.roles.description":
        "Review the fixed role model.",
      "organizations.settings.roles.owner.title": "Owner",
      "organizations.settings.roles.admin.title": "Administrator",
      "organizations.settings.roles.member.title": "Member",
    };
    return (key: string) => values[`${namespace}.${key}`] ?? key;
  },
}));
jest.mock("@/src/lib/api/auth/server/load-server-auth-session", () => ({
  loadServerAuthSession: jest.fn(),
}));
jest.mock("@/src/lib/api/organizations/server/load-organization", () => ({
  loadOrganization: jest.fn(),
}));
jest.mock("@/src/lib/api/organizations/server/load-organizations", () => ({
  loadOrganizations: jest.fn(),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/organizations/browser/organization-mutations", () => ({
  addBrowserOrganizationMember: jest.fn(),
  deleteBrowserOrganization: jest.fn(),
  updateBrowserOrganization: jest.fn(),
  updateBrowserOrganizationMemberRole: jest.fn(),
}));
jest.mock("@/src/lib/api/generated/sdk.gen", () => ({
  getOrganizationMembers: jest.fn(),
}));
jest.mock(
  "@/src/lib/api/organizations/server/load-organization-members",
  () => ({
    loadOrganizationMembers: jest.fn(),
  }),
);
jest.mock(
  "@/src/app/(site)/@organizationSwitcher/w/[organizationKey]/workspace-organization-switcher",
  () => ({
    WorkspaceOrganizationSwitcherSlot: jest.fn(({ params }) => (
      <i data-params={String(params)}>workspace switcher</i>
    )),
  }),
);

const loadSession = jest.mocked(loadServerAuthSession);
const loadDetail = jest.mocked(loadOrganization);
const loadList = jest.mocked(loadOrganizations);
const loadMembers = jest.mocked(loadOrganizationMembers);
const addMember = jest.mocked(addBrowserOrganizationMember);
const deleteOrganization = jest.mocked(deleteBrowserOrganization);
const getMembers = jest.mocked(getOrganizationMembers);
const updateOrganization = jest.mocked(updateBrowserOrganization);
const updateMemberRole = jest.mocked(updateBrowserOrganizationMemberRole);
const capabilities = {
  canUpdateOrganization: true,
  canDeleteOrganization: true,
  canAddMembers: true,
  canUpdateMemberRoles: true,
  canManageTeams: true,
  canManageInvitations: true,
};
const acme = {
  id: "01900000-0000-7000-8000-000000000010",
  name: "Acme",
  slug: "acme",
  canonicalKey: "acme",
  createdAt: "2026-07-30T10:00:00Z",
  updatedAt: "2026-07-30T10:00:00Z",
  currentRole: "owner",
  capabilities,
} satisfies OrganizationSummaryResponse;
const detail = {
  ...acme,
  allowedEmailDomains: ["example.com"],
} satisfies OrganizationDetailResponse;
const currentMember = {
  id: "01900000-0000-7000-8000-000000000030",
  userId: "user-id",
  name: "Current User",
  email: "current@example.com",
  imageUrl: null,
  role: "owner",
  joinedAt: "2026-07-30T10:00:00Z",
  emailDomain: "example.com",
  isOutsideAllowedEmailDomains: false,
} satisfies OrganizationMemberResponse;

function findElementByType(
  node: ReactNode,
  type: ReactElement["type"],
): ReactElement | null {
  if (!isValidElement(node)) {
    return null;
  }
  if (node.type === type) {
    return node;
  }
  const children = (node.props as { children?: ReactNode }).children;
  for (const child of Array.isArray(children) ? children : [children]) {
    const match = findElementByType(child, type);
    if (match) {
      return match;
    }
  }
  return null;
}

async function loadWorkspaceSettingsForm(
  organization: OrganizationDetailResponse,
): Promise<ReactElement> {
  loadDetail.mockResolvedValueOnce({ ok: true, data: organization });
  const workspace = await WorkspacePage({
    params: Promise.resolve({ organizationKey: organization.canonicalKey }),
  });
  const settingsForm = findElementByType(workspace, OrganizationSettingsForm);
  expect(settingsForm).not.toBeNull();
  return settingsForm!;
}

async function loadUsersMemberDirectory(
  organization: OrganizationDetailResponse,
  page: OrganizationMemberPageResponse,
): Promise<ReactElement> {
  loadDetail.mockResolvedValueOnce({ ok: true, data: organization });
  loadMembers.mockResolvedValueOnce({ ok: true, data: page });
  const users = await UsersPage({
    params: Promise.resolve({ organizationKey: organization.canonicalKey }),
  });
  const directory = findElementByType(users, OrganizationMemberDirectory);
  expect(directory).not.toBeNull();
  return directory!;
}

async function loadWorkspaceDeleteDialog(
  organization: OrganizationDetailResponse,
): Promise<ReactElement> {
  loadDetail.mockResolvedValueOnce({ ok: true, data: organization });
  loadList.mockResolvedValueOnce({
    ok: true,
    data: {
      items: [
        organization,
        {
          ...acme,
          id: "01900000-0000-7000-8000-000000000091",
          name: "Other",
          slug: "other",
          canonicalKey: "other",
        },
      ],
      nextCursor: null,
    },
  });
  const workspace = await WorkspacePage({
    params: Promise.resolve({ organizationKey: organization.canonicalKey }),
  });
  const dialog = findElementByType(workspace, OrganizationDeleteDialog);
  expect(dialog).not.toBeNull();
  return dialog!;
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((next) => {
    resolve = next;
  });
  return { promise, resolve };
}

function memberPageResult(
  items: readonly OrganizationMemberResponse[],
  nextCursor: string | null,
): Awaited<ReturnType<typeof getOrganizationMembers>> {
  return {
    data: { data: { items: [...items], nextCursor } },
  } as Awaited<ReturnType<typeof getOrganizationMembers>>;
}

function LayoutCommitHarness({
  children,
  onCommit,
}: Readonly<{ children: ReactNode; onCommit?: () => void }>) {
  useLayoutEffect(() => {
    onCommit?.();
  }, [onCommit]);
  return children;
}

beforeEach(() => {
  jest.clearAllMocks();
  pathname.mockReturnValue("/w/acme/settings/workspace");
  loadSession.mockResolvedValue({
    ok: true,
    data: {
      authenticated: true,
      user: {
        id: "user-id",
        name: "Current User",
        email: "current@example.com",
        emailVerified: true,
        image: null,
      },
      session: {
        id: "session-id",
        createdAt: "2026-07-30T10:00:00Z",
        updatedAt: "2026-07-30T10:00:00Z",
        expiresAt: "2026-08-01T10:00:00Z",
        activeOrganizationId: detail.id,
      },
    },
  });
  loadDetail.mockResolvedValue({ ok: true, data: detail });
  loadList.mockResolvedValue({
    ok: true,
    data: { items: [acme], nextCursor: null },
  });
  loadMembers.mockResolvedValue({
    ok: true,
    data: { items: [currentMember], nextCursor: null },
  });
});

it("exposes Teams to every member and Invitations to invitation managers", () => {
  renderWithMessages(
    <OrganizationSettingsNav
      canManageInvitations
      organizationKey="acme"
      pathname="/w/acme/settings/users"
    />,
  );

  const nav = screen.getByRole("navigation", { name: "Workspace settings" });
  expect(within(nav).getByRole("link", { name: "Workspace" })).toHaveAttribute(
    "href",
    "/w/acme/settings/workspace",
  );
  expect(within(nav).getByRole("link", { name: "Users" })).toHaveAttribute(
    "aria-current",
    "page",
  );
  expect(within(nav).getByRole("link", { name: "Roles" })).toBeVisible();
  expect(within(nav).getByRole("link", { name: "Teams" })).toHaveAttribute(
    "href",
    "/w/acme/settings/teams",
  );
  expect(
    within(nav).getByRole("link", { name: "Invitations" }),
  ).toHaveAttribute("href", "/w/acme/settings/invitations");
  expect(within(nav).queryByText("API Keys")).not.toBeInTheDocument();
});

it("keeps Teams visible but hides Invitations without the server capability", () => {
  renderWithMessages(
    <OrganizationSettingsNav
      canManageInvitations={false}
      organizationKey="acme"
      pathname="/w/acme/settings/teams"
    />,
  );

  const nav = screen.getByRole("navigation", { name: "Workspace settings" });
  expect(within(nav).getByRole("link", { name: "Teams" })).toHaveAttribute(
    "aria-current",
    "page",
  );
  expect(within(nav).queryByText("Invitations")).not.toBeInTheDocument();
});

it("canonicalizes settings root to the returned workspace settings URL", async () => {
  loadDetail.mockResolvedValue({
    ok: true,
    data: { ...detail, canonicalKey: "canonical-acme" },
  });

  await expect(
    SettingsPage({ params: Promise.resolve({ organizationKey: detail.id }) }),
  ).rejects.toThrow("NEXT_REDIRECT:/w/canonical-acme/settings/workspace");
});

it("uses a non-disclosing forbidden state for inaccessible settings when another workspace exists", async () => {
  loadDetail.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "organization_not_found",
      status: 404,
    },
  });

  await expect(
    AuthenticatedOrganizationSettingsShell({
      children: <p>secret settings</p>,
      params: Promise.resolve({ organizationKey: "foreign" }),
    }),
  ).rejects.toThrow("NEXT_FORBIDDEN");
});

it("lets each child segment own its exact anonymous login return URL", async () => {
  loadSession.mockResolvedValue({
    ok: true,
    data: {
      authenticated: false,
      user: null,
      session: null,
    },
  });

  const shell = await AuthenticatedOrganizationSettingsShell({
    children: <p>child owns authentication</p>,
    params: Promise.resolve({ organizationKey: "acme" }),
  });
  render(shell);
  expect(screen.getByText("child owns authentication")).toBeVisible();

  for (const [page, destination] of [
    [WorkspacePage, "/w/acme/settings/workspace"],
    [UsersPage, "/w/acme/settings/users"],
    [RolesPage, "/w/acme/settings/roles"],
  ] as const) {
    await expect(
      page({ params: Promise.resolve({ organizationKey: "acme" }) }),
    ).rejects.toThrow(
      `NEXT_REDIRECT:/auth/login?redirect=${encodeURIComponent(destination)}`,
    );
  }
});

it("renders role-aware workspace, users, and fixed-role explanation pages", async () => {
  const workspace = await WorkspacePage({
    params: Promise.resolve({ organizationKey: "acme" }),
  });
  renderWithMessages(workspace);
  expect(
    screen.getByRole("heading", { name: "Workspace settings" }),
  ).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "Delete workspace" }),
  ).not.toBeInTheDocument();

  const users = await UsersPage({
    params: Promise.resolve({ organizationKey: "acme" }),
  });
  renderWithMessages(users);
  expect(
    screen.getByRole("heading", { name: "Workspace users" }),
  ).toBeVisible();
  expect(screen.getByText("Current User")).toBeVisible();

  const roles = await RolesPage({
    params: Promise.resolve({ organizationKey: "acme" }),
  });
  renderWithMessages(roles);
  expect(
    screen.getByRole("heading", { name: "Workspace roles" }),
  ).toBeVisible();
  expect(screen.getByRole("heading", { name: "Owner" })).toBeVisible();
  expect(screen.getByRole("heading", { name: "Administrator" })).toBeVisible();
  expect(screen.getByRole("heading", { name: "Member" })).toBeVisible();
  expect(
    screen.queryByRole("button", { name: /create role/i }),
  ).not.toBeInTheDocument();
});

it("serializes compact actor, organization, and member views into the users client boundary", async () => {
  const users = (await UsersPage({
    params: Promise.resolve({ organizationKey: "acme" }),
  })) as ReactElement<{ children: ReactElement[] }>;
  const directory = users.props.children[1] as ReactElement<{
    currentActor: unknown;
    initialPage: unknown;
    organization: unknown;
  }>;

  expect(directory.props.currentActor).toEqual({
    userId: "user-id",
    name: "Current User",
    email: "current@example.com",
    role: "owner",
    isOutsideAllowedEmailDomains: false,
  });
  expect(directory.props.organization).toEqual({
    id: detail.id,
    currentRole: "owner",
    capabilities: {
      canAddMembers: true,
      canUpdateMemberRoles: true,
    },
  });
  expect(directory.props.initialPage).toEqual({
    items: [
      {
        id: currentMember.id,
        userId: currentMember.userId,
        name: currentMember.name,
        email: currentMember.email,
        role: currentMember.role,
        joinedAt: currentMember.joinedAt,
        isOutsideAllowedEmailDomains:
          currentMember.isOutsideAllowedEmailDomains,
      },
    ],
    nextCursor: null,
  });
});

it.each([
  ["first@second@example.com", true],
  ["person name@example.com", true],
  ["person@example", true],
  ["person@-bad.example.com", true],
  ["person@bad-.example.com", true],
  [`person@${"a".repeat(64)}.com`, true],
  [" Person@Example.COM ", false],
  ["person@sub.example.com", true],
] as const)(
  "matches backend actor domain eligibility for %s",
  async (email, expectedOutsidePolicy) => {
    loadSession.mockResolvedValue({
      ok: true,
      data: {
        authenticated: true,
        user: {
          id: "user-id",
          name: "Current User",
          email,
          emailVerified: true,
          image: null,
        },
        session: {
          id: "session-id",
          createdAt: "2026-07-30T10:00:00Z",
          updatedAt: "2026-07-30T10:00:00Z",
          expiresAt: "2026-08-01T10:00:00Z",
          activeOrganizationId: detail.id,
        },
      },
    });

    const users = (await UsersPage({
      params: Promise.resolve({ organizationKey: "acme" }),
    })) as ReactElement<{ children: ReactElement[] }>;
    const directory = users.props.children[1] as ReactElement<{
      currentActor: Record<string, unknown>;
    }>;

    expect(directory.props.currentActor).toEqual({
      userId: "user-id",
      name: "Current User",
      email,
      role: "owner",
      isOutsideAllowedEmailDomains: expectedOutsidePolicy,
    });
    expect(directory.props.currentActor).not.toHaveProperty("emailDomain");
  },
);

it("keys the member directory client boundary by the resolved organization id", async () => {
  const directory = await loadUsersMemberDirectory(detail, {
    items: [currentMember],
    nextCursor: null,
  });

  expect(directory.key).toBe(detail.id);
});

it("isolates member state and transports when one pathname resolves to a different organization id", async () => {
  const memberA = {
    ...currentMember,
    id: "01900000-0000-7000-8000-000000000031",
    userId: "01900000-0000-7000-8000-000000000021",
    name: "Member A",
    email: "member-a@example.com",
    role: "member" as const,
  } satisfies OrganizationMemberResponse;
  const tailA = {
    ...memberA,
    id: "01900000-0000-7000-8000-000000000032",
    userId: "01900000-0000-7000-8000-000000000022",
    name: "Tail A",
    email: "tail-a@example.com",
  } satisfies OrganizationMemberResponse;
  const freshA = {
    ...memberA,
    id: "01900000-0000-7000-8000-000000000033",
    userId: "01900000-0000-7000-8000-000000000023",
    name: "Fresh A",
    email: "fresh-a@example.com",
  } satisfies OrganizationMemberResponse;
  const organizationB = {
    ...detail,
    id: "01900000-0000-7000-8000-000000000099",
    name: "Replacement",
    allowedEmailDomains: ["replacement.example.com"],
  } satisfies OrganizationDetailResponse;
  const currentMemberB = {
    ...currentMember,
    id: "01900000-0000-7000-8000-000000000039",
    email: "current@replacement.example.com",
  } satisfies OrganizationMemberResponse;
  const memberB = {
    ...memberA,
    id: "01900000-0000-7000-8000-000000000041",
    userId: "01900000-0000-7000-8000-000000000051",
    name: "Member B",
    email: "member-b@replacement.example.com",
  } satisfies OrganizationMemberResponse;
  const tailB = {
    ...memberB,
    id: "01900000-0000-7000-8000-000000000042",
    userId: "01900000-0000-7000-8000-000000000052",
    name: "Tail B",
    email: "tail-b@replacement.example.com",
  } satisfies OrganizationMemberResponse;
  const addedB = {
    ...memberB,
    id: "01900000-0000-7000-8000-000000000043",
    userId: "01900000-0000-7000-8000-000000000053",
    name: "Outside B",
    email: "outside-b@external.test",
    isOutsideAllowedEmailDomains: true,
  } satisfies OrganizationMemberResponse;
  const acknowledgementUserA = "01900000-0000-7000-8000-000000000061";
  const acknowledgementUserB = addedB.userId;
  const activeReadA =
    deferred<Awaited<ReturnType<typeof getOrganizationMembers>>>();
  const acknowledgementB =
    deferred<Awaited<ReturnType<typeof addBrowserOrganizationMember>>>();
  let organizationARefreshes = 0;

  getMembers.mockImplementation((options) => {
    const organizationId = options.path.organizationId;
    const cursor = options.query?.cursor;
    if (organizationId === detail.id && cursor === "cursor-a") {
      return Promise.resolve(
        memberPageResult([tailA], "cursor-a-tail"),
      ) as never;
    }
    if (organizationId === detail.id && cursor === "cursor-a-tail") {
      return Promise.resolve({
        error: { code: "internal_error", traceId: "trace-a-load" },
        response: { status: 500 } as Response,
      } as Awaited<ReturnType<typeof getOrganizationMembers>>) as never;
    }
    if (organizationId === detail.id && cursor === undefined) {
      organizationARefreshes += 1;
      if (organizationARefreshes === 1) {
        return Promise.resolve({
          error: { code: "internal_error", traceId: "trace-a-refresh" },
          response: { status: 500 } as Response,
        } as Awaited<ReturnType<typeof getOrganizationMembers>>) as never;
      }
      return activeReadA.promise as never;
    }
    if (organizationId === organizationB.id && cursor === "cursor-b") {
      return Promise.resolve(memberPageResult([tailB], null)) as never;
    }
    if (organizationId === organizationB.id && cursor === undefined) {
      return Promise.resolve(
        memberPageResult([currentMemberB, memberB, addedB], null),
      ) as never;
    }
    throw new Error(`Unexpected member read for ${organizationId}/${cursor}`);
  });
  updateMemberRole.mockResolvedValue({
    ok: true,
    data: { ...memberA, role: "admin" },
  });
  addMember.mockImplementation((_client, organizationId, body) => {
    if (organizationId === detail.id) {
      return Promise.resolve({
        ok: false,
        failure: {
          kind: "problem",
          code: "member_domain_acknowledgement_required",
          status: 409,
          traceId: "trace-a-domain",
          email: "outside-a@external.test",
          emailDomain: "external.test",
          allowedEmailDomains: ["example.com"],
        },
      });
    }
    if (organizationId === organizationB.id) {
      return body.acknowledgeDomainRestriction
        ? Promise.resolve({ ok: true, data: addedB })
        : acknowledgementB.promise;
    }
    throw new Error(`Unexpected add-member organization ${organizationId}`);
  });

  const firstDirectory = await loadUsersMemberDirectory(detail, {
    items: [currentMember, memberA],
    nextCursor: "cursor-a",
  });
  const view = renderWithMessages(firstDirectory);

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  expect(await screen.findByText("Tail A")).toBeVisible();

  fireEvent.click(screen.getByRole("combobox", { name: "Role for Member A" }));
  fireEvent.click(screen.getByRole("option", { name: "Administrator" }));
  expect(await screen.findByText("trace-a-refresh")).toBeVisible();
  expect(
    screen.getByRole("combobox", { name: "Role for Member A" }),
  ).toHaveTextContent("Administrator");

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  expect(await screen.findByText("trace-a-load")).toBeVisible();

  fireEvent.click(
    screen.getByRole("button", {
      name: "Retry member directory refresh",
    }),
  );
  await waitFor(() => expect(getMembers).toHaveBeenCalledTimes(4));
  const activeSignalA = (
    getMembers.mock.calls[3]?.[0] as { signal: AbortSignal }
  ).signal;
  expect(activeSignalA.aborted).toBe(false);

  fireEvent.click(screen.getByRole("button", { name: "Add member" }));
  fireEvent.change(await screen.findByLabelText("User ID"), {
    target: { value: acknowledgementUserA },
  });
  fireEvent.click(screen.getByRole("button", { name: "Add" }));
  expect(await screen.findByText(/outside-a@external\.test/)).toBeVisible();
  expect(screen.getByRole("button", { name: "Confirm add" })).toBeEnabled();

  const sameIdDirectory = await loadUsersMemberDirectory(
    { ...detail, name: "Acme refreshed" },
    {
      items: [currentMember, freshA, { ...memberA, role: "member" }],
      nextCursor: "cursor-a-refreshed",
    },
  );
  view.rerender(withMessages(sameIdDirectory));

  expect(screen.getByText("Fresh A")).toBeVisible();
  expect(screen.getByText("Tail A")).toBeVisible();
  expect(screen.getByText("trace-a-load")).toBeVisible();
  expect(screen.getByText(/outside-a@external\.test/)).toBeVisible();
  expect(screen.getByLabelText("Role for Member A")).toHaveTextContent(
    "Administrator",
  );
  expect(screen.getByText("Loading members").closest("button")).toBeDisabled();
  expect(activeSignalA.aborted).toBe(false);
  expect(getMembers).toHaveBeenCalledTimes(4);

  const replacementDirectory = await loadUsersMemberDirectory(organizationB, {
    items: [currentMemberB, memberB],
    nextCursor: "cursor-b",
  });
  view.rerender(withMessages(replacementDirectory));

  await waitFor(() => expect(activeSignalA.aborted).toBe(true));
  expect(screen.getByText("Member B")).toBeVisible();
  expect(screen.queryByText("Member A")).not.toBeInTheDocument();
  expect(screen.queryByText("Tail A")).not.toBeInTheDocument();
  expect(screen.queryByText("trace-a-load")).not.toBeInTheDocument();
  expect(screen.queryByText("trace-a-refresh")).not.toBeInTheDocument();
  expect(
    screen.queryByText(/outside-a@external\.test/),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Confirm add" }),
  ).not.toBeInTheDocument();

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));
  expect(await screen.findByText("Tail B")).toBeVisible();
  expect(getMembers).toHaveBeenNthCalledWith(5, {
    client: { id: "browser-client" },
    cache: "no-store",
    path: { organizationId: organizationB.id },
    query: { cursor: "cursor-b" },
    signal: expect.anything(),
  });

  fireEvent.click(screen.getByRole("button", { name: "Add member" }));
  fireEvent.change(await screen.findByLabelText("User ID"), {
    target: { value: acknowledgementUserB },
  });
  fireEvent.click(screen.getByRole("button", { name: "Add" }));

  await waitFor(() => expect(addMember).toHaveBeenCalledTimes(2));
  expect(addMember).toHaveBeenNthCalledWith(
    2,
    { id: "browser-client" },
    organizationB.id,
    { userId: acknowledgementUserB, role: "member" },
  );
  expect(
    addMember.mock.calls.some(
      ([, organizationId, body]) =>
        organizationId === organizationB.id &&
        body.acknowledgeDomainRestriction === true,
    ),
  ).toBe(false);
  expect(
    screen.queryByRole("button", { name: "Confirm add" }),
  ).not.toBeInTheDocument();

  await act(async () => {
    acknowledgementB.resolve({
      ok: false,
      failure: {
        kind: "problem",
        code: "member_domain_acknowledgement_required",
        status: 409,
        traceId: "trace-b-domain",
        email: addedB.email,
        emailDomain: "external.test",
        allowedEmailDomains: ["replacement.example.com"],
      },
    });
    await acknowledgementB.promise;
  });

  expect(await screen.findByText(/outside-b@external\.test/)).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "Confirm add" }));
  await waitFor(() => expect(addMember).toHaveBeenCalledTimes(3));
  expect(addMember).toHaveBeenNthCalledWith(
    3,
    { id: "browser-client" },
    organizationB.id,
    {
      userId: acknowledgementUserB,
      role: "member",
      acknowledgeDomainRestriction: true,
    },
  );
  expect(await screen.findByText("Outside B")).toBeVisible();
  expect(getMembers).toHaveBeenNthCalledWith(6, {
    client: { id: "browser-client" },
    cache: "no-store",
    path: { organizationId: organizationB.id },
    signal: expect.anything(),
  });
});

it.each(["add", "role"] as const)(
  "does not let a pending organization-A %s completion start an A refresh after B replaces the keyed directory",
  async (action) => {
    const memberA = {
      ...currentMember,
      id: "01900000-0000-7000-8000-000000000071",
      userId: "01900000-0000-7000-8000-000000000081",
      name: "Pending Member A",
      email: "pending-a@example.com",
      role: "member" as const,
    } satisfies OrganizationMemberResponse;
    const lateAddedA = {
      ...memberA,
      id: "01900000-0000-7000-8000-000000000072",
      userId: "01900000-0000-7000-8000-000000000082",
      name: "Late Added A",
      email: "late-added-a@example.com",
    } satisfies OrganizationMemberResponse;
    const organizationB = {
      ...detail,
      id: "01900000-0000-7000-8000-000000000098",
      name: "Replacement B",
      allowedEmailDomains: ["replacement.example.com"],
    } satisfies OrganizationDetailResponse;
    const currentMemberB = {
      ...currentMember,
      id: "01900000-0000-7000-8000-000000000079",
      email: "current@replacement.example.com",
    } satisfies OrganizationMemberResponse;
    const memberB = {
      ...memberA,
      id: "01900000-0000-7000-8000-000000000073",
      userId: "01900000-0000-7000-8000-000000000083",
      name: "Member B after pending A",
      email: "member-b@replacement.example.com",
    } satisfies OrganizationMemberResponse;
    const pendingAdd =
      deferred<Awaited<ReturnType<typeof addBrowserOrganizationMember>>>();
    const pendingRole =
      deferred<
        Awaited<ReturnType<typeof updateBrowserOrganizationMemberRole>>
      >();
    addMember.mockReturnValue(pendingAdd.promise);
    updateMemberRole.mockReturnValue(pendingRole.promise);
    getMembers.mockResolvedValue(
      memberPageResult([currentMember, memberA, lateAddedA], null) as never,
    );
    const firstDirectory = await loadUsersMemberDirectory(detail, {
      items: [currentMember, memberA],
      nextCursor: null,
    });
    const view = renderWithMessages(firstDirectory);

    if (action === "add") {
      fireEvent.click(screen.getByRole("button", { name: "Add member" }));
      fireEvent.change(await screen.findByLabelText("User ID"), {
        target: { value: lateAddedA.userId },
      });
      fireEvent.click(screen.getByRole("button", { name: "Add" }));
      await waitFor(() => expect(addMember).toHaveBeenCalledTimes(1));
    } else {
      fireEvent.click(
        screen.getByRole("combobox", { name: "Role for Pending Member A" }),
      );
      fireEvent.click(screen.getByRole("option", { name: "Administrator" }));
      await waitFor(() => expect(updateMemberRole).toHaveBeenCalledTimes(1));
    }

    const replacementDirectory = await loadUsersMemberDirectory(organizationB, {
      items: [currentMemberB, memberB],
      nextCursor: null,
    });
    view.rerender(withMessages(replacementDirectory));
    expect(screen.getByText("Member B after pending A")).toBeVisible();

    await act(async () => {
      if (action === "add") {
        pendingAdd.resolve({ ok: true, data: lateAddedA });
        await pendingAdd.promise;
      } else {
        pendingRole.resolve({
          ok: true,
          data: { ...memberA, role: "admin" },
        });
        await pendingRole.promise;
      }
      await Promise.resolve();
    });

    expect(getMembers).not.toHaveBeenCalled();
    expect(screen.getByText("Member B after pending A")).toBeVisible();
    expect(screen.queryByText("Late Added A")).not.toBeInTheDocument();
  },
);

it.each(["add", "role"] as const)(
  "lets a pending %s completion reconcile the live same-ID directory after refreshed capabilities remove the leaf control",
  async (action) => {
    const memberA = {
      ...currentMember,
      id: "01900000-0000-7000-8000-000000000074",
      userId: "01900000-0000-7000-8000-000000000084",
      name: "Capability Member A",
      email: "capability-a@example.com",
      role: "member" as const,
    } satisfies OrganizationMemberResponse;
    const confirmedMember = {
      ...memberA,
      ...(action === "add"
        ? {
            id: "01900000-0000-7000-8000-000000000075",
            userId: "01900000-0000-7000-8000-000000000085",
            name: "Capability Added A",
            email: "capability-added-a@example.com",
          }
        : { role: "admin" as const }),
    } satisfies OrganizationMemberResponse;
    const pendingAdd =
      deferred<Awaited<ReturnType<typeof addBrowserOrganizationMember>>>();
    const pendingRole =
      deferred<
        Awaited<ReturnType<typeof updateBrowserOrganizationMemberRole>>
      >();
    addMember.mockReturnValue(pendingAdd.promise);
    updateMemberRole.mockReturnValue(pendingRole.promise);
    getMembers.mockResolvedValue(
      memberPageResult([currentMember, confirmedMember], null) as never,
    );
    const firstDirectory = await loadUsersMemberDirectory(detail, {
      items: [currentMember, memberA],
      nextCursor: null,
    });
    const view = renderWithMessages(firstDirectory);

    if (action === "add") {
      fireEvent.click(screen.getByRole("button", { name: "Add member" }));
      fireEvent.change(await screen.findByLabelText("User ID"), {
        target: { value: confirmedMember.userId },
      });
      fireEvent.click(screen.getByRole("button", { name: "Add" }));
      await waitFor(() => expect(addMember).toHaveBeenCalledTimes(1));
    } else {
      fireEvent.click(
        screen.getByRole("combobox", { name: "Role for Capability Member A" }),
      );
      fireEvent.click(screen.getByRole("option", { name: "Administrator" }));
      await waitFor(() => expect(updateMemberRole).toHaveBeenCalledTimes(1));
    }

    const capabilityRefresh = await loadUsersMemberDirectory(
      {
        ...detail,
        capabilities: {
          ...detail.capabilities,
          canAddMembers: false,
          canUpdateMemberRoles: false,
          canManageTeams: false,
          canManageInvitations: false,
        },
      },
      { items: [currentMember, memberA], nextCursor: null },
    );
    view.rerender(withMessages(capabilityRefresh));
    expect(
      screen.queryByRole("button", { name: "Add member" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("combobox", {
        name: "Role for Capability Member A",
      }),
    ).not.toBeInTheDocument();

    await act(async () => {
      if (action === "add") {
        pendingAdd.resolve({ ok: true, data: confirmedMember });
        await pendingAdd.promise;
      } else {
        pendingRole.resolve({ ok: true, data: confirmedMember });
        await pendingRole.promise;
      }
      await Promise.resolve();
    });

    await waitFor(() => expect(getMembers).toHaveBeenCalledTimes(1));
    expect(getMembers).toHaveBeenCalledWith({
      client: { id: "browser-client" },
      cache: "no-store",
      path: { organizationId: detail.id },
      signal: expect.anything(),
    });
    expect(await screen.findByRole("status")).toHaveTextContent(
      action === "add" ? "Member added." : "Member role updated.",
    );
  },
);

it("aborts a recovery read created while Activity-hidden when a different organization replaces the keyed directory", async () => {
  const memberA = {
    ...currentMember,
    id: "01900000-0000-7000-8000-000000000076",
    userId: "01900000-0000-7000-8000-000000000086",
    name: "Hidden Member A",
    email: "hidden-a@example.com",
    role: "member" as const,
  } satisfies OrganizationMemberResponse;
  const hiddenAddedA = {
    ...memberA,
    id: "01900000-0000-7000-8000-000000000077",
    userId: "01900000-0000-7000-8000-000000000087",
    name: "Hidden Added A",
    email: "hidden-added-a@example.com",
  } satisfies OrganizationMemberResponse;
  const organizationB = {
    ...detail,
    id: "01900000-0000-7000-8000-000000000097",
    name: "Hidden Replacement B",
  } satisfies OrganizationDetailResponse;
  const pendingAdd =
    deferred<Awaited<ReturnType<typeof addBrowserOrganizationMember>>>();
  const pendingRead =
    deferred<Awaited<ReturnType<typeof getOrganizationMembers>>>();
  addMember.mockReturnValue(pendingAdd.promise);
  getMembers.mockReturnValue(pendingRead.promise as never);
  const firstDirectory = await loadUsersMemberDirectory(detail, {
    items: [currentMember, memberA],
    nextCursor: null,
  });
  const view = renderWithMessages(
    <Activity mode="visible">{firstDirectory}</Activity>,
  );

  fireEvent.click(screen.getByRole("button", { name: "Add member" }));
  fireEvent.change(await screen.findByLabelText("User ID"), {
    target: { value: hiddenAddedA.userId },
  });
  fireEvent.click(screen.getByRole("button", { name: "Add" }));
  await waitFor(() => expect(addMember).toHaveBeenCalledTimes(1));

  view.rerender(
    withMessages(<Activity mode="hidden">{firstDirectory}</Activity>),
  );
  await act(async () => {
    pendingAdd.resolve({ ok: true, data: hiddenAddedA });
    await pendingAdd.promise;
    await Promise.resolve();
  });
  await waitFor(() => expect(getMembers).toHaveBeenCalledTimes(1));
  const hiddenReadSignal = (
    getMembers.mock.calls[0]?.[0] as { signal: AbortSignal }
  ).signal;
  expect(hiddenReadSignal.aborted).toBe(false);

  const replacementDirectory = await loadUsersMemberDirectory(organizationB, {
    items: [
      {
        ...currentMember,
        id: "01900000-0000-7000-8000-000000000078",
        email: "current@hidden-replacement.example.com",
      },
    ],
    nextCursor: null,
  });
  view.rerender(
    withMessages(<Activity mode="hidden">{replacementDirectory}</Activity>),
  );

  await waitFor(() => expect(hiddenReadSignal.aborted).toBe(true));
});

it("passes only id and name through the workspace delete client boundary", async () => {
  loadList.mockResolvedValue({
    ok: true,
    data: {
      items: [
        acme,
        {
          ...acme,
          id: "01900000-0000-7000-8000-000000000099",
          name: "Other",
          canonicalKey: "other",
          slug: "other",
        },
      ],
      nextCursor: null,
    },
  });

  const workspace = await WorkspacePage({
    params: Promise.resolve({ organizationKey: "acme" }),
  });
  const deleteDialog = findElementByType(workspace, OrganizationDeleteDialog);

  expect(deleteDialog?.props).toEqual({
    canDelete: true,
    organization: { id: detail.id, name: "Acme" },
  });
});

it("keys the workspace delete client boundary by the resolved organization id", async () => {
  const deleteDialog = await loadWorkspaceDeleteDialog(detail);

  expect(deleteDialog.key).toBe(detail.id);
});

it("drops delete confirmation and pending identity when one pathname resolves to another organization", async () => {
  const replacementOrganization = {
    ...detail,
    id: "01900000-0000-7000-8000-000000000095",
    name: "Acme",
  } satisfies OrganizationDetailResponse;
  const oldDelete =
    deferred<Awaited<ReturnType<typeof deleteBrowserOrganization>>>();
  deleteOrganization.mockReturnValueOnce(oldDelete.promise);
  const firstDialog = await loadWorkspaceDeleteDialog(detail);
  const view = renderWithMessages(firstDialog);

  fireEvent.click(screen.getByRole("button", { name: "Delete workspace" }));
  fireEvent.change(await screen.findByLabelText('Type "Acme" to confirm'), {
    target: { value: "Acme" },
  });
  fireEvent.click(
    screen.getByRole("button", { name: "Permanently delete workspace" }),
  );
  expect(
    await screen.findByRole("button", { name: "Deleting workspace" }),
  ).toBeDisabled();
  expect(deleteOrganization).toHaveBeenCalledWith(
    { id: "browser-client" },
    detail.id,
    { confirmationName: "Acme" },
  );

  const replacementDialog = await loadWorkspaceDeleteDialog(
    replacementOrganization,
  );
  view.rerender(withMessages(replacementDialog));

  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Deleting workspace" }),
  ).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Delete workspace" }));
  expect(await screen.findByLabelText('Type "Acme" to confirm')).toHaveValue(
    "",
  );
  expect(
    screen.getByRole("button", { name: "Permanently delete workspace" }),
  ).toBeDisabled();

  await act(async () => {
    oldDelete.resolve({
      ok: false,
      failure: {
        kind: "problem",
        code: "concurrency_conflict",
        status: 409,
        traceId: "trace-old-delete",
      },
    });
    await oldDelete.promise;
  });

  expect(screen.queryByText("trace-old-delete")).not.toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "Permanently delete workspace" }),
  ).toBeDisabled();
  expect(deleteOrganization).not.toHaveBeenCalledWith(
    expect.anything(),
    replacementOrganization.id,
    expect.anything(),
  );
});

it("ignores late successful delete effects from organization A after B replaces the keyed dialog", async () => {
  const replacementOrganization = {
    ...detail,
    id: "01900000-0000-7000-8000-000000000094",
    name: "Acme",
  } satisfies OrganizationDetailResponse;
  const oldDelete =
    deferred<Awaited<ReturnType<typeof deleteBrowserOrganization>>>();
  deleteOrganization.mockReturnValueOnce(oldDelete.promise);
  const firstDialog = await loadWorkspaceDeleteDialog(detail);
  const view = renderWithMessages(firstDialog);

  fireEvent.click(screen.getByRole("button", { name: "Delete workspace" }));
  fireEvent.change(await screen.findByLabelText('Type "Acme" to confirm'), {
    target: { value: "Acme" },
  });
  fireEvent.click(
    screen.getByRole("button", { name: "Permanently delete workspace" }),
  );
  await waitFor(() => expect(deleteOrganization).toHaveBeenCalledTimes(1));

  const replacementDialog = await loadWorkspaceDeleteDialog(
    replacementOrganization,
  );
  view.rerender(withMessages(replacementDialog));
  fireEvent.click(screen.getByRole("button", { name: "Delete workspace" }));
  expect(await screen.findByRole("dialog")).toBeVisible();

  await act(async () => {
    oldDelete.resolve({ ok: true, data: { organizationId: detail.id } });
    await oldDelete.promise;
    await Promise.resolve();
  });

  expect(screen.getByRole("dialog")).toBeVisible();
  expect(replace).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();
});

it("keys the workspace settings client boundary by the resolved organization id", async () => {
  const settingsForm = await loadWorkspaceSettingsForm(detail);

  expect(settingsForm.key).toBe(detail.id);
});

it("remounts dirty workspace settings when the same slug resolves to a different organization while preserving same-id refresh state", async () => {
  const replacementOrganization = {
    ...detail,
    id: "01900000-0000-7000-8000-000000000099",
    name: "Replacement",
    allowedEmailDomains: ["replacement.example.com"],
    currentRole: "member",
    capabilities: {
      ...capabilities,
      canUpdateOrganization: false,
      canDeleteOrganization: false,
      canAddMembers: false,
      canUpdateMemberRoles: false,
      canManageTeams: false,
      canManageInvitations: false,
    },
  } satisfies OrganizationDetailResponse;
  const firstForm = await loadWorkspaceSettingsForm(detail);
  const view = renderWithMessages(firstForm);

  fireEvent.change(screen.getByLabelText("Workspace Name"), {
    target: { value: "Dirty Acme" },
  });
  fireEvent.change(screen.getByLabelText("Workspace Slug"), {
    target: { value: "invalid slug" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Use 1–64 lowercase letters or numbers separated by single hyphens.",
  );

  const replacementForm = await loadWorkspaceSettingsForm(
    replacementOrganization,
  );
  view.rerender(withMessages(replacementForm));

  expect(screen.getByLabelText("Workspace Name")).toHaveValue("Replacement");
  expect(screen.getByLabelText("Workspace Slug")).toHaveValue("acme");
  expect(screen.getByLabelText("Allowed Email Domains")).toHaveValue(
    "replacement.example.com",
  );
  expect(
    screen.queryByText(
      "Use 1–64 lowercase letters or numbers separated by single hyphens.",
    ),
  ).not.toBeInTheDocument();
  expect(screen.getByLabelText("Workspace Name")).toBeDisabled();
  expect(
    screen.queryByRole("button", { name: "Save" }),
  ).not.toBeInTheDocument();

  const promotedReplacement = {
    ...replacementOrganization,
    name: "Replacement on server",
    currentRole: "admin",
    capabilities,
  } satisfies OrganizationDetailResponse;
  const promotedForm = await loadWorkspaceSettingsForm(promotedReplacement);
  view.rerender(withMessages(promotedForm));

  expect(screen.getByLabelText("Workspace Name")).toHaveValue("Replacement");
  expect(screen.getByLabelText("Workspace Name")).toBeEnabled();
  expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();

  updateOrganization.mockResolvedValue({
    ok: true,
    data: {
      ...promotedReplacement,
      name: "Replacement saved",
    },
  });
  fireEvent.change(screen.getByLabelText("Workspace Name"), {
    target: { value: "Replacement saved" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  await waitFor(() => {
    expect(updateOrganization).toHaveBeenCalledWith(
      { id: "browser-client" },
      replacementOrganization.id,
      { name: "Replacement saved" },
    );
  });
  expect(updateOrganization).not.toHaveBeenCalledWith(
    expect.anything(),
    detail.id,
    expect.anything(),
  );
});

it("drops an old pending form identity when the same slug resolves to another organization", async () => {
  const replacementOrganization = {
    ...detail,
    id: "01900000-0000-7000-8000-000000000098",
    name: "Replacement",
    allowedEmailDomains: ["replacement.example.com"],
  } satisfies OrganizationDetailResponse;
  let resolveOldUpdate!: (
    value: Awaited<ReturnType<typeof updateBrowserOrganization>>,
  ) => void;
  const oldUpdate = new Promise<
    Awaited<ReturnType<typeof updateBrowserOrganization>>
  >((resolve) => {
    resolveOldUpdate = resolve;
  });
  updateOrganization.mockReturnValueOnce(oldUpdate).mockResolvedValueOnce({
    ok: true,
    data: {
      ...replacementOrganization,
      name: "Replacement saved",
    },
  });
  const firstForm = await loadWorkspaceSettingsForm(detail);
  const view = renderWithMessages(firstForm);

  fireEvent.change(screen.getByLabelText("Workspace Name"), {
    target: { value: "Acme pending" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));
  expect(await screen.findByRole("button", { name: "Saving" })).toBeDisabled();
  expect(updateOrganization).toHaveBeenNthCalledWith(
    1,
    { id: "browser-client" },
    detail.id,
    { name: "Acme pending" },
  );

  const replacementForm = await loadWorkspaceSettingsForm(
    replacementOrganization,
  );
  view.rerender(withMessages(replacementForm));

  expect(screen.getByLabelText("Workspace Name")).toHaveValue("Replacement");
  expect(screen.getByLabelText("Workspace Name")).toBeEnabled();
  expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  expect(
    screen.queryByRole("button", { name: "Saving" }),
  ).not.toBeInTheDocument();

  fireEvent.change(screen.getByLabelText("Workspace Name"), {
    target: { value: "Replacement saved" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));
  await waitFor(() => {
    expect(updateOrganization).toHaveBeenNthCalledWith(
      2,
      { id: "browser-client" },
      replacementOrganization.id,
      { name: "Replacement saved" },
    );
  });

  await act(async () => {
    resolveOldUpdate({
      ok: false,
      failure: {
        kind: "problem",
        code: "concurrency_conflict",
        status: 409,
        traceId: "old-acme-trace",
      },
    });
    await oldUpdate;
  });

  expect(screen.getByLabelText("Workspace Name")).toHaveValue(
    "Replacement saved",
  );
  expect(screen.queryByText("old-acme-trace")).not.toBeInTheDocument();
});

it("ignores a late successful update from the organization replaced at the same slug", async () => {
  const replacementOrganization = {
    ...detail,
    id: "01900000-0000-7000-8000-000000000097",
    name: "Replacement",
    allowedEmailDomains: ["replacement.example.com"],
  } satisfies OrganizationDetailResponse;
  let resolveOldUpdate!: (
    value: Awaited<ReturnType<typeof updateBrowserOrganization>>,
  ) => void;
  const oldUpdate = new Promise<
    Awaited<ReturnType<typeof updateBrowserOrganization>>
  >((resolve) => {
    resolveOldUpdate = resolve;
  });
  updateOrganization.mockReturnValueOnce(oldUpdate).mockResolvedValueOnce({
    ok: true,
    data: {
      ...replacementOrganization,
      name: "Replacement saved",
    },
  });
  const firstForm = await loadWorkspaceSettingsForm(detail);
  const view = renderWithMessages(firstForm);

  fireEvent.change(screen.getByLabelText("Workspace Slug"), {
    target: { value: "bar" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));
  expect(await screen.findByRole("button", { name: "Saving" })).toBeDisabled();
  expect(updateOrganization).toHaveBeenNthCalledWith(
    1,
    { id: "browser-client" },
    detail.id,
    { slug: "bar" },
  );

  const replacementForm = await loadWorkspaceSettingsForm(
    replacementOrganization,
  );
  view.rerender(withMessages(replacementForm));
  fireEvent.change(screen.getByLabelText("Workspace Name"), {
    target: { value: "Replacement saved" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  expect(await screen.findByRole("status")).toHaveTextContent(
    "Workspace settings saved.",
  );
  expect(updateOrganization).toHaveBeenNthCalledWith(
    2,
    { id: "browser-client" },
    replacementOrganization.id,
    { name: "Replacement saved" },
  );
  expect(replace).not.toHaveBeenCalled();
  expect(refresh).toHaveBeenCalledTimes(1);

  await act(async () => {
    resolveOldUpdate({
      ok: true,
      data: {
        ...detail,
        slug: "bar",
        canonicalKey: "bar",
      },
    });
    await oldUpdate;
  });

  expect(screen.getByLabelText("Workspace Name")).toHaveValue(
    "Replacement saved",
  );
  expect(screen.getByRole("status")).toHaveTextContent(
    "Workspace settings saved.",
  );
  expect(replace).not.toHaveBeenCalled();
  expect(refresh).toHaveBeenCalledTimes(1);
});

it("invalidates the old organization during the different-id commit before passive cleanup", async () => {
  const replacementOrganization = {
    ...detail,
    id: "01900000-0000-7000-8000-000000000096",
    name: "Replacement",
    allowedEmailDomains: ["replacement.example.com"],
  } satisfies OrganizationDetailResponse;
  let resolveOldUpdate!: (
    value: Awaited<ReturnType<typeof updateBrowserOrganization>>,
  ) => void;
  const oldUpdate = new Promise<
    Awaited<ReturnType<typeof updateBrowserOrganization>>
  >((resolve) => {
    resolveOldUpdate = resolve;
  });
  updateOrganization.mockReturnValueOnce(oldUpdate).mockResolvedValueOnce({
    ok: true,
    data: {
      ...replacementOrganization,
      name: "Replacement saved",
    },
  });
  const firstForm = await loadWorkspaceSettingsForm(detail);
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  const reactActEnvironment = globalThis as typeof globalThis & {
    IS_REACT_ACT_ENVIRONMENT?: boolean;
  };
  const actEnvironment = reactActEnvironment.IS_REACT_ACT_ENVIRONMENT;

  try {
    await act(async () => {
      root.render(
        withMessages(<LayoutCommitHarness>{firstForm}</LayoutCommitHarness>),
      );
    });
    fireEvent.change(screen.getByLabelText("Workspace Slug"), {
      target: { value: "bar" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(
      await screen.findByRole("button", { name: "Saving" }),
    ).toBeDisabled();

    const replacementForm = await loadWorkspaceSettingsForm(
      replacementOrganization,
    );
    let signalReplacementCommit!: () => void;
    const replacementCommitted = new Promise<void>((resolve) => {
      signalReplacementCommit = resolve;
    });
    reactActEnvironment.IS_REACT_ACT_ENVIRONMENT = false;
    root.render(
      withMessages(
        <LayoutCommitHarness
          onCommit={() => {
            resolveOldUpdate({
              ok: true,
              data: {
                ...detail,
                slug: "bar",
                canonicalKey: "bar",
              },
            });
            signalReplacementCommit();
          }}
        >
          {replacementForm}
        </LayoutCommitHarness>,
      ),
    );
    await replacementCommitted;
    await Promise.resolve();
    reactActEnvironment.IS_REACT_ACT_ENVIRONMENT = actEnvironment;

    expect(screen.getByLabelText("Workspace Name")).toHaveValue("Replacement");
    expect(replace).not.toHaveBeenCalled();
    expect(refresh).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText("Workspace Name"), {
      target: { value: "Replacement saved" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("status")).toHaveTextContent(
      "Workspace settings saved.",
    );
    expect(updateOrganization).toHaveBeenNthCalledWith(
      2,
      { id: "browser-client" },
      replacementOrganization.id,
      { name: "Replacement saved" },
    );
    expect(replace).not.toHaveBeenCalled();
    expect(refresh).toHaveBeenCalledTimes(1);
  } finally {
    reactActEnvironment.IS_REACT_ACT_ENVIRONMENT = actEnvironment;
    await act(async () => {
      root.unmount();
    });
    container.remove();
  }
});

it("adds explicit workspace switcher slot pages for every settings destination", async () => {
  const params = Promise.resolve({ organizationKey: "acme" });
  for (const page of [
    SettingsSwitcherSlot,
    WorkspaceSwitcherSlot,
    UsersSwitcherSlot,
    RolesSwitcherSlot,
  ]) {
    render(await page({ params }));
  }

  expect(screen.getAllByText("workspace switcher")).toHaveLength(4);
});

it("keeps the settings layout as a server shell around its local suspense boundary", async () => {
  const result = await SettingsLayout({
    children: <p>settings child</p>,
    params: Promise.resolve({ organizationKey: "acme" }),
  });
  expect(result.type).toBe(Suspense);

  const shell = await AuthenticatedOrganizationSettingsShell({
    children: <p>settings child</p>,
    params: Promise.resolve({ organizationKey: "acme" }),
  });
  renderWithMessages(shell);
  expect(screen.getByText("settings child")).toBeInTheDocument();
});
