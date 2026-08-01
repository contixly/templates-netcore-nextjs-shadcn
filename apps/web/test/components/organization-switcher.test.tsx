import { act, fireEvent, screen, waitFor } from "@testing-library/react";
import {
  Activity,
  StrictMode,
  useInsertionEffect,
  useLayoutEffect,
} from "react";
import { renderToString } from "react-dom/server";

import { OrganizationSwitcher } from "@/src/components/organizations/organization-switcher";
import { setActiveBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import type { OrganizationSummaryResponse } from "@/src/lib/api/generated/types.gen";
import { renderWithMessages, withMessages } from "@/test/support/render";

const organizationControlReadyAttribute =
  "data-organization-control-interaction-ready";

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
  canManageTeams: true,
  canManageInvitations: true,
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

function ActivityHideSignal({ onHidden }: Readonly<{ onHidden: () => void }>) {
  useLayoutEffect(() => () => onHidden(), [onHidden]);
  return null;
}

function ActivityPathnameCommitSignal({
  onCommitted,
  pathname: committedPathname,
}: Readonly<{
  onCommitted: (pathname: string) => void;
  pathname: string;
}>) {
  useInsertionEffect(() => {
    onCommitted(committedPathname);
  }, [committedPathname, onCommitted]);
  return null;
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((complete) => {
    resolve = complete;
  });
  return { promise, resolve };
}

beforeEach(() => {
  push.mockReset();
  refresh.mockReset();
  pathname.mockReset();
  setActive.mockReset();
  pathname.mockReturnValue("/w/old/settings/users");
});

it("keeps the switcher trigger unavailable in server HTML until its client handler is attached", async () => {
  const switcher = <OrganizationSwitcher organizations={organizations} />;
  const serverMarkup = renderToString(withMessages(switcher));
  const serverDocument = new DOMParser().parseFromString(
    serverMarkup,
    "text/html",
  );
  const serverTrigger = Array.from(
    serverDocument.querySelectorAll("button"),
  ).find((button) => button.textContent?.includes("Current workspace: Old"));

  expect(serverTrigger?.hasAttribute("disabled")).toBe(true);
  expect(serverTrigger?.getAttribute(organizationControlReadyAttribute)).toBe(
    null,
  );

  renderWithMessages(switcher);
  const trigger = screen.getByRole("button", {
    name: "Current workspace: Old",
  });
  await waitFor(() => {
    expect(trigger).toHaveAttribute(organizationControlReadyAttribute, "true");
  });
  expect(trigger).toBeEnabled();

  fireEvent.click(trigger);
  expect(
    await screen.findByRole("button", { name: "Switch to New" }),
  ).toBeVisible();
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

it("replaces a stale same-id list entry with the authoritative current detail", async () => {
  renderWithMessages(
    <OrganizationSwitcher
      currentOrganization={{
        id: "old-id",
        name: "Renamed Workspace",
        canonicalKey: "old",
      }}
      organizations={organizations}
    />,
  );

  const trigger = screen.getByRole("button", {
    name: "Current workspace: Renamed Workspace",
  });
  expect(
    screen.queryByRole("button", { name: "Current workspace: Old" }),
  ).not.toBeInTheDocument();

  fireEvent.click(trigger);

  expect(
    await screen.findByRole("button", {
      name: "Switch to Renamed Workspace",
    }),
  ).toHaveAttribute("aria-current", "true");
  expect(
    screen.queryByRole("button", { name: "Switch to Old" }),
  ).not.toBeInTheDocument();
  expect(screen.getAllByRole("listitem")).toHaveLength(2);
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

it("sets active context before preserving a registered route and refreshing once after StrictMode replay", async () => {
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
  renderWithMessages(
    <StrictMode>
      <OrganizationSwitcher organizations={organizations} />
    </StrictMode>,
  );

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
  expect(setActive).toHaveBeenCalledTimes(1);
  expect(push).toHaveBeenCalledTimes(1);
  expect(order).toEqual(["mutation", "navigation", "refresh"]);
});

it("makes a successful continuation inert after permanent deletion", async () => {
  const pendingSwitch =
    deferred<Awaited<ReturnType<typeof setActiveBrowserOrganization>>>();
  setActive.mockReturnValue(pendingSwitch.promise);
  const view = renderWithMessages(
    <OrganizationSwitcher organizations={organizations} />,
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  fireEvent.click(await screen.findByRole("button", { name: "Switch to New" }));
  await waitFor(() => expect(setActive).toHaveBeenCalledTimes(1));

  view.unmount();
  await act(async () => {
    pendingSwitch.resolve({
      ok: true,
      data: { organizationId: "new-id" },
    });
    await pendingSwitch.promise;
  });

  expect(push).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();
});

it("settles an Activity-hidden switch and refreshes once without replaying stale push on reveal", async () => {
  const pendingSwitch =
    deferred<Awaited<ReturnType<typeof setActiveBrowserOrganization>>>();
  const hidden = jest.fn();
  setActive.mockReturnValue(pendingSwitch.promise);
  const switcher = (
    <>
      <OrganizationSwitcher organizations={organizations} />
      <ActivityHideSignal onHidden={hidden} />
    </>
  );
  const view = renderWithMessages(
    <Activity mode="visible">{switcher}</Activity>,
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  fireEvent.click(await screen.findByRole("button", { name: "Switch to New" }));
  await waitFor(() => expect(setActive).toHaveBeenCalledTimes(1));

  view.rerender(withMessages(<Activity mode="hidden">{switcher}</Activity>));
  expect(hidden).toHaveBeenCalledTimes(1);
  await act(async () => {
    pendingSwitch.resolve({
      ok: true,
      data: { organizationId: "new-id" },
    });
    await pendingSwitch.promise;
  });
  expect(push).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();

  view.rerender(withMessages(<Activity mode="visible">{switcher}</Activity>));
  expect(
    screen.queryByRole("heading", { name: "Switch workspace" }),
  ).not.toBeInTheDocument();
  expect(push).not.toHaveBeenCalled();
  expect(refresh).toHaveBeenCalledTimes(1);

  view.rerender(withMessages(<Activity mode="hidden">{switcher}</Activity>));
  view.rerender(withMessages(<Activity mode="visible">{switcher}</Activity>));
  expect(push).not.toHaveBeenCalled();
  expect(refresh).toHaveBeenCalledTimes(1);
});

it("discards a queued hidden refresh on permanent deletion", async () => {
  const pendingSwitch =
    deferred<Awaited<ReturnType<typeof setActiveBrowserOrganization>>>();
  const hidden = jest.fn();
  setActive.mockReturnValue(pendingSwitch.promise);
  const switcher = (
    <>
      <OrganizationSwitcher organizations={organizations} />
      <ActivityHideSignal onHidden={hidden} />
    </>
  );
  const view = renderWithMessages(
    <Activity mode="visible">{switcher}</Activity>,
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  fireEvent.click(await screen.findByRole("button", { name: "Switch to New" }));
  await waitFor(() => expect(setActive).toHaveBeenCalledTimes(1));
  view.rerender(withMessages(<Activity mode="hidden">{switcher}</Activity>));
  expect(hidden).toHaveBeenCalledTimes(1);
  await act(async () => {
    pendingSwitch.resolve({
      ok: true,
      data: { organizationId: "new-id" },
    });
    await pendingSwitch.promise;
  });

  view.unmount();
  expect(push).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();
});

it("discards a queued Activity-hidden refresh after the mounted pathname changes away and back", async () => {
  const pendingSwitch =
    deferred<Awaited<ReturnType<typeof setActiveBrowserOrganization>>>();
  const committedPathname = jest.fn();
  const hidden = jest.fn();
  setActive.mockReturnValueOnce(pendingSwitch.promise).mockResolvedValueOnce({
    ok: true,
    data: { organizationId: "new-id" },
  });
  const originalPathname = "/w/old/settings/users";
  const otherPathname = "/w/new/settings/roles";
  const renderActivity = (mode: "hidden" | "visible", signalPathname: string) =>
    withMessages(
      <Activity mode={mode}>
        <OrganizationSwitcher organizations={organizations} />
        <ActivityHideSignal onHidden={hidden} />
        <ActivityPathnameCommitSignal
          onCommitted={committedPathname}
          pathname={signalPathname}
        />
      </Activity>,
    );
  const view = renderWithMessages(
    <Activity mode="visible">
      <OrganizationSwitcher organizations={organizations} />
      <ActivityHideSignal onHidden={hidden} />
      <ActivityPathnameCommitSignal
        onCommitted={committedPathname}
        pathname={originalPathname}
      />
    </Activity>,
  );
  expect(committedPathname).toHaveBeenLastCalledWith(originalPathname);

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  fireEvent.click(await screen.findByRole("button", { name: "Switch to New" }));
  await waitFor(() => expect(setActive).toHaveBeenCalledTimes(1));

  view.rerender(renderActivity("hidden", originalPathname));
  expect(hidden).toHaveBeenCalledTimes(1);
  await act(async () => {
    pendingSwitch.resolve({
      ok: true,
      data: { organizationId: "new-id" },
    });
    await pendingSwitch.promise;
  });
  expect(push).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();

  pathname.mockReturnValue(otherPathname);
  view.rerender(renderActivity("hidden", otherPathname));
  expect(committedPathname).toHaveBeenLastCalledWith(otherPathname);
  pathname.mockReturnValue(originalPathname);
  view.rerender(renderActivity("hidden", originalPathname));
  expect(committedPathname).toHaveBeenLastCalledWith(originalPathname);
  expect(committedPathname).toHaveBeenCalledTimes(3);

  view.rerender(renderActivity("visible", originalPathname));
  expect(push).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  fireEvent.click(await screen.findByRole("button", { name: "Switch to New" }));
  await waitFor(() => {
    expect(setActive).toHaveBeenCalledTimes(2);
    expect(push).toHaveBeenCalledWith("/w/new/settings/users");
  });
  expect(push).toHaveBeenCalledTimes(1);
  expect(refresh).toHaveBeenCalledTimes(1);
});

it("reconciles a safe Activity-hidden failure and unlocks a visible retry", async () => {
  const pendingSwitch =
    deferred<Awaited<ReturnType<typeof setActiveBrowserOrganization>>>();
  const hidden = jest.fn();
  setActive.mockReturnValueOnce(pendingSwitch.promise).mockResolvedValueOnce({
    ok: true,
    data: { organizationId: "new-id" },
  });
  const switcher = (
    <>
      <OrganizationSwitcher organizations={organizations} />
      <ActivityHideSignal onHidden={hidden} />
    </>
  );
  const view = renderWithMessages(
    <Activity mode="visible">{switcher}</Activity>,
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  fireEvent.click(await screen.findByRole("button", { name: "Switch to New" }));
  await waitFor(() => expect(setActive).toHaveBeenCalledTimes(1));
  view.rerender(withMessages(<Activity mode="hidden">{switcher}</Activity>));
  expect(hidden).toHaveBeenCalledTimes(1);
  await act(async () => {
    pendingSwitch.resolve({
      ok: false,
      failure: {
        kind: "problem",
        code: "organization_permission_denied",
        status: 403,
        traceId: "hidden-switch-failure",
      },
    });
    await pendingSwitch.promise;
  });
  expect(push).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();

  view.rerender(withMessages(<Activity mode="visible">{switcher}</Activity>));
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "hidden-switch-failure",
  );
  expect(
    screen.queryByText("organization_permission_denied"),
  ).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Switch to New" }));
  await waitFor(() => {
    expect(setActive).toHaveBeenCalledTimes(2);
    expect(push).toHaveBeenCalledWith("/w/new/settings/users");
  });
  expect(push).toHaveBeenCalledTimes(1);
  expect(refresh).toHaveBeenCalledTimes(1);
});

it("permanently suppresses an old switch after the mounted pathname changes away and back", async () => {
  const firstSwitch =
    deferred<Awaited<ReturnType<typeof setActiveBrowserOrganization>>>();
  setActive.mockReturnValueOnce(firstSwitch.promise).mockResolvedValueOnce({
    ok: true,
    data: { organizationId: "new-id" },
  });
  const renderSwitcher = () =>
    withMessages(<OrganizationSwitcher organizations={organizations} />);
  const view = renderWithMessages(
    <OrganizationSwitcher organizations={organizations} />,
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  fireEvent.click(await screen.findByRole("button", { name: "Switch to New" }));
  await waitFor(() => expect(setActive).toHaveBeenCalledTimes(1));

  pathname.mockReturnValue("/w/new/settings/roles");
  view.rerender(renderSwitcher());
  expect(screen.getByRole("button", { name: "Switch to New" })).toHaveAttribute(
    "aria-current",
    "true",
  );
  pathname.mockReturnValue("/w/old/settings/users");
  view.rerender(renderSwitcher());

  await act(async () => {
    firstSwitch.resolve({
      ok: true,
      data: { organizationId: "new-id" },
    });
    await firstSwitch.promise;
  });
  expect(push).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  fireEvent.click(await screen.findByRole("button", { name: "Switch to New" }));
  await waitFor(() => {
    expect(setActive).toHaveBeenCalledTimes(2);
    expect(push).toHaveBeenCalledWith("/w/new/settings/users");
  });
  expect(push).toHaveBeenCalledTimes(1);
  expect(refresh).toHaveBeenCalledTimes(1);
});

it("settles a route-exited failure safely and allows the current route to retry", async () => {
  const firstSwitch =
    deferred<Awaited<ReturnType<typeof setActiveBrowserOrganization>>>();
  setActive.mockReturnValueOnce(firstSwitch.promise).mockResolvedValueOnce({
    ok: true,
    data: { organizationId: "old-id" },
  });
  const view = renderWithMessages(
    <OrganizationSwitcher organizations={organizations} />,
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  fireEvent.click(await screen.findByRole("button", { name: "Switch to New" }));
  await waitFor(() => expect(setActive).toHaveBeenCalledTimes(1));

  pathname.mockReturnValue("/w/new/settings/roles");
  view.rerender(
    withMessages(<OrganizationSwitcher organizations={organizations} />),
  );
  await act(async () => {
    firstSwitch.resolve({
      ok: false,
      failure: {
        kind: "problem",
        code: "organization_permission_denied",
        status: 403,
        traceId: "route-exit-switch-failure",
      },
    });
    await firstSwitch.promise;
  });
  expect(push).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();
  expect(
    screen.queryByText("organization_permission_denied"),
  ).not.toBeInTheDocument();

  fireEvent.click(screen.getByRole("button", { name: "Switch to Old" }));
  await waitFor(() => {
    expect(setActive).toHaveBeenCalledTimes(2);
    expect(push).toHaveBeenCalledWith("/w/old/settings/roles");
  });
  expect(push).toHaveBeenCalledTimes(1);
  expect(refresh).toHaveBeenCalledTimes(1);
});

it("persists an explicitly selected routed organization when the session preference differs", async () => {
  setActive.mockResolvedValue({
    ok: true,
    data: { organizationId: "old-id" },
  });
  renderWithMessages(
    <OrganizationSwitcher
      activeOrganizationId="new-id"
      organizations={organizations}
    />,
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  fireEvent.click(await screen.findByRole("button", { name: "Switch to Old" }));

  await waitFor(() => {
    expect(setActive).toHaveBeenCalledWith(
      { id: "browser-client" },
      { organizationId: "old-id" },
    );
    expect(push).toHaveBeenCalledWith("/w/old/settings/users");
    expect(refresh).toHaveBeenCalledTimes(1);
  });
  expect(
    screen.queryByRole("heading", { name: "Switch workspace" }),
  ).not.toBeInTheDocument();
});

it("closes without transport only when the routed and active organizations both match", async () => {
  renderWithMessages(
    <OrganizationSwitcher
      activeOrganizationId="old-id"
      organizations={organizations}
    />,
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Current workspace: Old" }),
  );
  fireEvent.click(await screen.findByRole("button", { name: "Switch to Old" }));

  await waitFor(() => {
    expect(
      screen.queryByRole("heading", { name: "Switch workspace" }),
    ).not.toBeInTheDocument();
  });
  expect(setActive).not.toHaveBeenCalled();
  expect(push).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();
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

it("routes truncated switcher results to the canonical client-paged workspace list", async () => {
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
  ).toHaveAttribute("href", "/workspaces");
});

it.each([
  ["Manage workspaces", undefined],
  ["Load more workspaces", "opaque cursor"],
] as const)(
  "closes the controlled switcher when %s navigation starts",
  async (linkName, nextCursor) => {
    renderWithMessages(
      <OrganizationSwitcher
        nextCursor={nextCursor}
        organizations={organizations}
      />,
    );

    fireEvent.click(
      screen.getByRole("button", { name: "Current workspace: Old" }),
    );
    const link = await screen.findByRole("link", { name: linkName });
    link.addEventListener("click", (event) => event.preventDefault());
    fireEvent.click(link);

    await waitFor(() => {
      expect(
        screen.queryByRole("heading", { name: "Switch workspace" }),
      ).not.toBeInTheDocument();
    });
  },
);

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
