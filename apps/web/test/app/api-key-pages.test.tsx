import { isValidElement, type ReactElement, type ReactNode } from "react";
import { screen } from "@testing-library/react";

import OrganizationApiKeySwitcherSlot from "@/src/app/(site)/@organizationSwitcher/w/[organizationKey]/settings/api-keys/page";
import ApiKeyPage from "@/src/app/(site)/user/api-keys/page";
import ApiKeyLoading from "@/src/app/(site)/user/api-keys/loading";
import OrganizationSwitcherSlot from "@/src/app/(site)/@organizationSwitcher/user/api-keys/page";
import OrganizationApiKeysError from "@/src/app/(site)/w/[organizationKey]/settings/api-keys/error";
import OrganizationApiKeysLoading from "@/src/app/(site)/w/[organizationKey]/settings/api-keys/loading";
import OrganizationApiKeysPage from "@/src/app/(site)/w/[organizationKey]/settings/api-keys/page";
import { ApiKeyManagement } from "@/src/components/api-keys/api-key-management";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { loadApiKeys } from "@/src/lib/api/api-keys/server/load-api-keys";
import type { OrganizationDetailResponse } from "@/src/lib/api/generated/types.gen";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { apiKeyPage } from "@/test/components/api-keys/fixtures";
import { renderWithMessages } from "@/test/support/render";

jest.mock("next/server", () => ({
  connection: jest.fn().mockResolvedValue(undefined),
}));
jest.mock("next/navigation", () => ({
  redirect: jest.fn((href: string) => {
    throw new Error(`NEXT_REDIRECT:${href}`);
  }),
}));
jest.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => {
    const values: Record<string, string> = {
      "apiKeys.page.title": "API keys",
      "apiKeys.page.description": "Personal automation credentials",
      "apiKeys.page.loading": "Loading API keys",
      "apiKeys.page.failureTitle": "API keys are unavailable",
      "apiKeys.page.failureDescription": "Try again",
      "apiKeys.page.organizationDescription":
        "Organization automation credentials",
    };
    return (key: string) => values[`${namespace}.${key}`] ?? key;
  },
}));
jest.mock("@/src/lib/api/api-keys/server/load-api-keys", () => ({
  loadApiKeys: jest.fn(),
}));
jest.mock("@/src/features/authentication/load-protected-session", () => ({
  loadProtectedSession: jest.fn(),
}));
jest.mock("@/src/lib/api/organizations/server/load-organization", () => ({
  loadOrganization: jest.fn(),
}));
jest.mock(
  "@/src/app/(site)/@organizationSwitcher/w/[organizationKey]/workspace-organization-switcher",
  () => ({
    WorkspaceOrganizationSwitcherSlot: jest.fn(({ params }) => (
      <i data-params={String(params)}>workspace switcher</i>
    )),
  }),
);
jest.mock("@/src/components/api-keys/api-key-management", () => ({
  ApiKeyManagement: ({
    initialPage,
    owner,
  }: {
    initialPage: { items: unknown[] };
    owner: unknown;
  }) => (
    <section
      data-owner={JSON.stringify(owner)}
      data-testid="api-key-management"
    >
      {initialPage.items.length}
    </section>
  ),
}));

const loadKeys = jest.mocked(loadApiKeys);
const loadSession = jest.mocked(loadProtectedSession);
const loadDetail = jest.mocked(loadOrganization);
const organizationId = "01900000-0000-7000-8000-000000000010";
const detail = {
  id: organizationId,
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
  allowedEmailDomains: [],
} satisfies OrganizationDetailResponse;

function findElementByType(
  node: ReactNode,
  type: ReactElement["type"],
): ReactElement | null {
  if (!isValidElement(node)) return null;
  if (node.type === type) return node;
  const children = (node.props as { children?: ReactNode }).children;
  for (const child of Array.isArray(children) ? children : [children]) {
    const match = findElementByType(child, type);
    if (match) return match;
  }
  return null;
}

beforeEach(() => {
  jest.clearAllMocks();
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
        expiresAt: "2026-08-03T10:00:00Z",
        activeOrganizationId: organizationId,
      },
    },
  });
  loadDetail.mockResolvedValue({ ok: true, data: detail });
});

it("loads exactly the first personal page on the server", async () => {
  loadKeys.mockResolvedValue({ ok: true, data: apiKeyPage });
  renderWithMessages(await ApiKeyPage());

  expect(loadKeys).toHaveBeenCalledTimes(1);
  expect(loadKeys).toHaveBeenCalledWith({ kind: "personal" }, { limit: 50 });
  expect(screen.getByRole("heading", { name: "API keys" })).toBeVisible();
  expect(screen.getByTestId("api-key-management")).toHaveTextContent("1");
});

it("renders a localized safe failure without exposing backend detail", async () => {
  loadKeys.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "internal_error",
      status: 500,
      traceId: "trace-safe",
    },
  });
  renderWithMessages(await ApiKeyPage());

  expect(screen.getByRole("alert")).toHaveTextContent(
    "API keys are unavailable",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-safe");
});

it("provides localized loading and a complete organization-switcher parallel slot", async () => {
  renderWithMessages(await ApiKeyLoading());
  expect(screen.getByRole("status")).toHaveTextContent("Loading API keys");
  expect(OrganizationSwitcherSlot()).toBeNull();
});

it("canonicalizes UUID organization routes before listing any API keys", async () => {
  await expect(
    OrganizationApiKeysPage({
      params: Promise.resolve({ organizationKey: organizationId }),
    }),
  ).rejects.toThrow("NEXT_REDIRECT:/w/acme/settings/api-keys");

  expect(loadDetail).toHaveBeenCalledWith(organizationId);
  expect(loadKeys).not.toHaveBeenCalled();
});

it.each(["owner", "admin"] as const)(
  "loads exactly the first organization page for an authorized %s from trusted detail identity and keys the client owner boundary",
  async (currentRole) => {
    loadDetail.mockResolvedValue({
      ok: true,
      data: { ...detail, currentRole },
    });
    loadKeys.mockResolvedValue({ ok: true, data: apiKeyPage });
    const page = await OrganizationApiKeysPage({
      params: Promise.resolve({ organizationKey: "acme" }),
    });
    const management = findElementByType(page, ApiKeyManagement);

    expect(loadSession).toHaveBeenCalledWith("/w/acme/settings/api-keys");
    expect(loadKeys).toHaveBeenCalledWith(
      {
        kind: "organization",
        organizationId,
        organizationKey: "acme",
        capabilities: { canManageApiKeys: true },
      },
      { limit: 50 },
    );
    expect(management?.key).toBe(organizationId);
    expect(management?.props).toEqual({
      initialPage: apiKeyPage,
      owner: {
        kind: "organization",
        organizationId,
        organizationKey: "acme",
        capabilities: { canManageApiKeys: true },
      },
    });

    renderWithMessages(page);
    expect(screen.getByRole("heading", { name: "API keys" })).toBeVisible();
    expect(
      screen.getByText("Organization automation credentials"),
    ).toBeVisible();
  },
);

it("renders the safe API-key failure and no rows or mutations when the organization list denies a member", async () => {
  loadDetail.mockResolvedValue({
    ok: true,
    data: {
      ...detail,
      currentRole: "member",
      capabilities: { ...detail.capabilities, canManageApiKeys: false },
    },
  });
  loadKeys.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "api_key_permission_denied",
      status: 403,
      traceId: "trace-member-denied",
    },
  });

  const page = await OrganizationApiKeysPage({
    params: Promise.resolve({ organizationKey: "acme" }),
  });
  renderWithMessages(page);

  expect(loadKeys).toHaveBeenCalledTimes(1);
  expect(screen.getByRole("alert")).toHaveTextContent(
    "API keys are unavailable",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-member-denied");
  expect(screen.queryByTestId("api-key-management")).not.toBeInTheDocument();
  expect(screen.queryByRole("row")).not.toBeInTheDocument();
  expect(screen.queryByRole("button")).not.toBeInTheDocument();
});

it("provides localized organization loading/error boundaries and its switcher slot", async () => {
  renderWithMessages(await OrganizationApiKeysLoading());
  expect(screen.getByRole("status")).toHaveTextContent("Loading API keys");

  const reset = jest.fn();
  renderWithMessages(
    <OrganizationApiKeysError
      error={new Error("private detail")}
      reset={reset}
    />,
  );
  expect(
    screen.getByRole("heading", { name: "Something went wrong" }),
  ).toBeVisible();

  const params = Promise.resolve({ organizationKey: "acme" });
  renderWithMessages(await OrganizationApiKeySwitcherSlot({ params }));
  expect(screen.getByText("workspace switcher")).toBeVisible();
});
