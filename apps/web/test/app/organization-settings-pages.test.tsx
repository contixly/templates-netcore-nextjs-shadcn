import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import {
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
import { OrganizationSettingsForm } from "@/src/components/organizations/organization-settings-form";
import { OrganizationSettingsNav } from "@/src/components/organizations/organization-settings-nav";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import type {
  OrganizationDetailResponse,
  OrganizationMemberResponse,
  OrganizationSummaryResponse,
} from "@/src/lib/api/generated/types.gen";
import { loadOrganizationMembers } from "@/src/lib/api/organizations/server/load-organization-members";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";
import { updateBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
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
  updateBrowserOrganization: jest.fn(),
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
const updateOrganization = jest.mocked(updateBrowserOrganization);
const capabilities = {
  canUpdateOrganization: true,
  canDeleteOrganization: true,
  canAddMembers: true,
  canUpdateMemberRoles: true,
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

it("exposes only Workspace, Users, and Roles settings navigation", () => {
  renderWithMessages(
    <OrganizationSettingsNav
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
  expect(within(nav).queryByText("Teams")).not.toBeInTheDocument();
  expect(within(nav).queryByText("Invitations")).not.toBeInTheDocument();
  expect(within(nav).queryByText("API Keys")).not.toBeInTheDocument();
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
