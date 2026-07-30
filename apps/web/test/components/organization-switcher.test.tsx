import { fireEvent, screen, waitFor } from "@testing-library/react";

import { OrganizationSwitcher } from "@/src/components/organizations/organization-switcher";
import { setActiveBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import type { OrganizationSummaryResponse } from "@/src/lib/api/generated/types.gen";
import { renderWithMessages } from "@/test/support/render";

const push = jest.fn();
const refresh = jest.fn();
const pathname = jest.fn(() => "/w/old/settings/users");

jest.mock("next/navigation", () => ({
  usePathname: () => pathname(),
  useRouter: () => ({ push, refresh }),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/organizations/browser/organization-mutations", () => ({
  setActiveBrowserOrganization: jest.fn(),
}));

const setActive = jest.mocked(setActiveBrowserOrganization);
const capabilities = {
  canUpdateOrganization: true,
  canDeleteOrganization: true,
  canAddMembers: true,
  canUpdateMemberRoles: true,
};
const organizations = [
  {
    id: "old-id",
    name: "Old",
    slug: "old",
    canonicalKey: "old",
    createdAt: "2026-07-30T10:00:00Z",
    updatedAt: "2026-07-30T10:00:00Z",
    currentRole: "owner",
    capabilities,
  },
  {
    id: "new-id",
    name: "New",
    slug: "new",
    canonicalKey: "new",
    createdAt: "2026-07-30T10:00:00Z",
    updatedAt: "2026-07-30T10:00:00Z",
    currentRole: "member",
    capabilities,
  },
] satisfies OrganizationSummaryResponse[];

const offPageCurrent = {
  id: "off-page-id",
  name: "Workspace Fifty One",
  canonicalKey: "workspace-fifty-one",
};

beforeEach(() => {
  jest.clearAllMocks();
  pathname.mockReturnValue("/w/old/settings/users");
});

it("does not render outside authenticated organization-aware paths", () => {
  pathname.mockReturnValue("/user/profile");
  renderWithMessages(<OrganizationSwitcher organizations={organizations} />);

  expect(
    screen.queryByRole("button", { name: /current workspace/i }),
  ).not.toBeInTheDocument();
});

it("uses the explicit current context when it is not in the first page", async () => {
  pathname.mockReturnValue("/w/workspace-fifty-one/dashboard");
  renderWithMessages(
    <OrganizationSwitcher
      currentOrganization={offPageCurrent}
      organizations={organizations}
    />,
  );

  fireEvent.click(
    screen.getByRole("button", {
      name: "Current workspace: Workspace Fifty One",
    }),
  );

  expect(
    await screen.findByRole("button", {
      name: "Switch to Workspace Fifty One",
    }),
  ).toHaveAttribute("aria-current", "true");
});

it("preserves the full accessible name while constraining long labels", () => {
  const longName = "A".repeat(50);
  pathname.mockReturnValue("/w/long/dashboard");
  renderWithMessages(
    <OrganizationSwitcher
      currentOrganization={{
        id: "long-id",
        name: longName,
        canonicalKey: "long",
      }}
      organizations={[{ id: "long-id", name: longName, canonicalKey: "long" }]}
    />,
  );

  expect(
    screen.getByRole("button", {
      name: `Current workspace: ${longName}`,
    }),
  ).toHaveClass("max-w-full");
});

it("sets active context before preserving a registered route and refreshing", async () => {
  const order: string[] = [];
  setActive.mockImplementation(async () => {
    order.push("mutation");
    return { ok: true, data: { organizationId: "new-id" } };
  });
  push.mockImplementation(() => {
    order.push("navigation");
  });
  refresh.mockImplementation(() => {
    order.push("refresh");
  });
  renderWithMessages(<OrganizationSwitcher organizations={organizations} />);

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  fireEvent.click(await screen.findByRole("button", { name: "Switch to New" }));

  await waitFor(() => {
    expect(setActive).toHaveBeenCalledWith(
      { id: "browser-client" },
      { organizationId: "new-id" },
    );
    expect(push).toHaveBeenCalledWith("/w/new/settings/users");
    expect(refresh).toHaveBeenCalledTimes(1);
  });
  expect(order).toEqual(["mutation", "navigation", "refresh"]);
});

it("falls back unknown deep paths to the selected dashboard", async () => {
  pathname.mockReturnValue("/w/old/custom/deep");
  setActive.mockResolvedValue({
    ok: true,
    data: { organizationId: "new-id" },
  });
  renderWithMessages(<OrganizationSwitcher organizations={organizations} />);

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  fireEvent.click(await screen.findByRole("button", { name: "Switch to New" }));

  await waitFor(() => {
    expect(push).toHaveBeenCalledWith("/w/new/dashboard");
  });
});

it("offers an explicit continuation when the first page is truncated", async () => {
  renderWithMessages(
    <OrganizationSwitcher
      nextCursor="opaque cursor"
      organizations={organizations}
    />,
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  expect(
    await screen.findByRole("link", { name: "Load more workspaces" }),
  ).toHaveAttribute("href", "/workspaces?cursor=opaque%20cursor");
});

it("keeps navigation blocked and shows safe failure copy when switching fails", async () => {
  setActive.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "organization_permission_denied",
      status: 403,
      traceId: "trace-switch",
    },
  });
  renderWithMessages(<OrganizationSwitcher organizations={organizations} />);

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  fireEvent.click(await screen.findByRole("button", { name: "Switch to New" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Unable to switch workspaces right now.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-switch");
  expect(
    screen.queryByText("organization_permission_denied"),
  ).not.toBeInTheDocument();
  expect(push).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();
});
