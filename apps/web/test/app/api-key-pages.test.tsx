import { isValidElement, type ReactElement, type ReactNode } from "react";
import { screen } from "@testing-library/react";

import OrganizationApiKeySwitcherSlot from "@/src/app/(protected)/@applicationNavigation/w/[organizationKey]/settings/api-keys/page";
import ApiKeyPage from "@/src/app/(protected)/user/api-keys/page";
import ApiKeyLoading from "@/src/app/(protected)/user/api-keys/loading";
import OrganizationSwitcherSlot from "@/src/app/(protected)/@applicationNavigation/user/api-keys/page";
import OrganizationApiKeysError from "@/src/app/(protected)/w/[organizationKey]/settings/api-keys/error";
import OrganizationApiKeysLoading from "@/src/app/(protected)/w/[organizationKey]/settings/api-keys/loading";
import OrganizationApiKeysPage from "@/src/app/(protected)/w/[organizationKey]/settings/api-keys/page";
import { ApiKeyManagement } from "@/src/features/api-keys/ui/api-key-management";
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
      "apiKeys.page.personalSectionTitle": "Personal API keys",
      "apiKeys.page.loading": "Loading API keys",
      "apiKeys.page.failureTitle": "API keys are unavailable",
      "apiKeys.page.failureDescription": "Try again",
      "apiKeys.page.organizationDescription":
        "Organization automation credentials",
      "apiKeys.page.organizationSectionTitle": "Organization API keys",
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
jest.mock("@/src/features/api-keys/ui/api-key-management", () => ({
  ApiKeyManagement: ({
    initialPage,
    owner,
  }: {
    initialPage: {
      items: Array<{ id: string; name: string; start: string }>;
      nextCursor?: string | null;
    };
    owner: unknown;
  }) => (
    <section
      data-owner={JSON.stringify(owner)}
      data-testid="api-key-management"
    >
      <span>{JSON.stringify(initialPage)}</span>
      <table>
        <tbody>
          {initialPage.items.map((apiKey) => (
            <tr key={apiKey.id}>
              <td>{apiKey.name}</td>
              <td>{apiKey.start}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <span data-testid="api-key-secret-view">Secret reveal view</span>
      <button type="button">Create API key</button>
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
  const view = renderWithMessages(await ApiKeyPage());

  expect(loadKeys).toHaveBeenCalledTimes(1);
  expect(loadKeys).toHaveBeenCalledWith({ kind: "personal" }, { limit: 50 });
  expect(
    screen.getByRole("heading", { level: 1, name: "API keys" }),
  ).toBeVisible();
  expect(
    screen
      .getByTestId("api-key-management")
      .closest('[data-slot="settings-page-section"]'),
  ).toHaveAttribute("data-mode", "wide");
  expect(screen.getByTestId("api-key-management")).toHaveTextContent("1");
  expect(
    Array.from(view.container.querySelectorAll("h1, h2"), (heading) =>
      heading.textContent?.trim(),
    ),
  ).toEqual(["API keys"]);
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

it("provides localized loading and the exact personal navigation return path", async () => {
  renderWithMessages(await ApiKeyLoading());
  expect(screen.getByRole("status")).toHaveTextContent("Loading API keys");
  expect(OrganizationSwitcherSlot().props).toEqual({
    redirectPath: "/user/api-keys",
  });
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

    const view = renderWithMessages(page);
    expect(
      screen.getByRole("heading", { level: 1, name: "API keys" }),
    ).toBeVisible();
    expect(
      screen.getByText("Organization automation credentials"),
    ).toBeVisible();
    expect(
      Array.from(view.container.querySelectorAll("h1, h2"), (heading) =>
        heading.textContent?.trim(),
      ),
    ).toEqual(["API keys"]);
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

it("fails closed after the actual list succeeds when the trusted API-key capability is false", async () => {
  const recognizableName = "Member must not see this organization key";
  const recognizableStart = "tk_org_forbidden";
  loadDetail.mockResolvedValue({
    ok: true,
    data: {
      ...detail,
      currentRole: "member",
      capabilities: { ...detail.capabilities, canManageApiKeys: false },
    },
  });
  loadKeys.mockResolvedValue({
    ok: true,
    data: {
      items: [
        {
          ...apiKeyPage.items[0],
          ownerKind: "organization",
          ownerId: organizationId,
          name: recognizableName,
          start: recognizableStart,
        },
      ],
      nextCursor: "must-not-render",
    },
  });

  const page = await OrganizationApiKeysPage({
    params: Promise.resolve({ organizationKey: "acme" }),
  });
  renderWithMessages(page);

  expect(loadKeys).toHaveBeenCalledTimes(1);
  expect(loadKeys).toHaveBeenCalledWith(
    {
      kind: "organization",
      organizationId,
      organizationKey: "acme",
      capabilities: { canManageApiKeys: false },
    },
    { limit: 50 },
  );
  expect(screen.getByRole("alert")).toHaveTextContent(
    "API keys are unavailable",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("Try again");
  expect(screen.queryByTestId("api-key-management")).not.toBeInTheDocument();
  expect(screen.queryByRole("row")).not.toBeInTheDocument();
  expect(document.body).not.toHaveTextContent(recognizableName);
  expect(document.body).not.toHaveTextContent(recognizableStart);
  expect(document.body).not.toHaveTextContent("must-not-render");
  expect(screen.queryByTestId("api-key-secret-view")).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Create API key" }),
  ).not.toBeInTheDocument();
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
  expect((await OrganizationApiKeySwitcherSlot({ params })).props).toEqual({
    redirectPath: "/w/acme/settings/api-keys",
    organizationKey: "acme",
  });
});
