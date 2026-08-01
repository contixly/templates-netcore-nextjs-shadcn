import {
  act,
  fireEvent,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";

import { OrganizationList } from "@/src/components/organizations/organization-list";
import { deleteBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import { getOrganizations } from "@/src/lib/api/generated/sdk.gen";
import type { OrganizationSummaryResponse } from "@/src/lib/api/generated/types.gen";
import { renderWithMessages, withMessages } from "@/test/support/render";
import { render } from "@testing-library/react";
import organizationsRu from "@/src/messages/organizations.ru.json";

jest.mock("next/navigation", () => ({
  useRouter: () => ({
    push: jest.fn(),
    replace: jest.fn(),
    refresh: jest.fn(),
  }),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/organizations/browser/organization-mutations", () => ({
  deleteBrowserOrganization: jest.fn(),
}));
jest.mock("@/src/lib/api/generated/sdk.gen", () => ({
  getOrganizations: jest.fn(),
}));

const deleteOrganization = jest.mocked(deleteBrowserOrganization);
const getOrganizationPages = jest.mocked(getOrganizations);

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

const beta = {
  ...acme,
  id: "01900000-0000-7000-8000-000000000011",
  name: "Beta",
  slug: "beta",
  canonicalKey: "beta",
  currentRole: "member",
  capabilities: {
    ...capabilities,
    canDeleteOrganization: false,
  },
} satisfies OrganizationSummaryResponse;

const gamma = {
  ...beta,
  id: "01900000-0000-7000-8000-000000000012",
  name: "Gamma",
  slug: "gamma",
  canonicalKey: "gamma",
} satisfies OrganizationSummaryResponse;

beforeEach(() => {
  jest.clearAllMocks();
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((next) => {
    resolve = next;
  });
  return { promise, resolve };
}

it("renders canonical dashboard/settings links without delete controls", () => {
  renderWithMessages(
    <OrganizationList initialPage={{ items: [acme], nextCursor: null }} />,
  );

  expect(
    screen.getByRole("button", { name: "Create New Workspace" }),
  ).toBeVisible();
  const card = screen.getByRole("article", { name: "Acme workspace" });
  expect(within(card).getByText("acme")).toBeVisible();
  expect(
    within(card).getByRole("link", { name: "Open workspace" }),
  ).toHaveAttribute("href", "/w/acme/dashboard");
  expect(within(card).getByRole("link", { name: "Settings" })).toHaveAttribute(
    "href",
    "/w/acme/settings/workspace",
  );
  expect(
    within(card).queryByRole("button", { name: /delete/i }),
  ).not.toBeInTheDocument();
});

it("offers delete only to capable owners when another workspace is accessible", () => {
  renderWithMessages(
    <OrganizationList
      initialPage={{ items: [acme, beta], nextCursor: null }}
    />,
  );

  expect(
    within(screen.getByRole("article", { name: "Acme workspace" })).getByRole(
      "button",
      { name: "Delete workspace" },
    ),
  ).toBeVisible();
  expect(
    within(screen.getByRole("article", { name: "Beta workspace" })).queryByRole(
      "button",
      { name: "Delete workspace" },
    ),
  ).not.toBeInTheDocument();
});

it("removes a confirmed deletion immediately and cannot resurrect it from stale refreshed pages", async () => {
  const otherOwner = {
    ...beta,
    currentRole: "owner" as const,
    capabilities,
  };
  deleteOrganization.mockResolvedValue({
    ok: true,
    data: { organizationId: acme.id },
  });
  const view = renderWithMessages(
    <OrganizationList
      initialPage={{ items: [acme, otherOwner], nextCursor: null }}
    />,
  );

  fireEvent.click(
    within(screen.getByRole("article", { name: "Acme workspace" })).getByRole(
      "button",
      { name: "Delete workspace" },
    ),
  );
  fireEvent.change(await screen.findByLabelText('Type "Acme" to confirm'), {
    target: { value: "Acme" },
  });
  fireEvent.click(
    screen.getByRole("button", { name: "Permanently delete workspace" }),
  );

  await waitFor(() => {
    expect(
      screen.queryByRole("article", { name: "Acme workspace" }),
    ).not.toBeInTheDocument();
  });
  expect(
    within(screen.getByRole("article", { name: "Beta workspace" })).queryByRole(
      "button",
      { name: "Delete workspace" },
    ),
  ).not.toBeInTheDocument();

  view.rerender(
    withMessages(
      <OrganizationList
        initialPage={{ items: [acme, otherOwner], nextCursor: null }}
      />,
    ),
  );
  expect(
    screen.queryByRole("article", { name: "Acme workspace" }),
  ).not.toBeInTheDocument();
});

it("localizes fixed organization roles instead of exposing API values", () => {
  render(
    <NextIntlClientProvider
      locale="ru"
      messages={{ organizations: organizationsRu }}
      timeZone="UTC"
    >
      <OrganizationList
        initialPage={{ items: [acme, beta], nextCursor: null }}
      />
    </NextIntlClientProvider>,
  );

  expect(screen.getByText("Роль: Владелец")).toBeVisible();
  expect(screen.getByText("Роль: Участник")).toBeVisible();
  expect(screen.queryByText("Роль: owner")).not.toBeInTheDocument();
  expect(screen.queryByText("Роль: member")).not.toBeInTheDocument();
});

it("appends generated pages and lets each incoming duplicate replace its older entry", async () => {
  getOrganizationPages.mockResolvedValue({
    data: {
      data: {
        items: [{ ...acme, name: "Authoritative Acme" }, beta],
        nextCursor: null,
      },
    },
  } as Awaited<ReturnType<typeof getOrganizations>>);

  renderWithMessages(
    <OrganizationList
      initialPage={{ items: [acme], nextCursor: "opaque-first" }}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "Load more workspaces" }));

  await waitFor(() => {
    expect(screen.getAllByRole("article")).toHaveLength(2);
  });
  expect(
    screen.getByRole("article", { name: "Authoritative Acme workspace" }),
  ).toBeVisible();
  expect(screen.getByRole("article", { name: "Beta workspace" })).toBeVisible();
  expect(
    screen.queryByRole("article", { name: "Acme workspace" }),
  ).not.toBeInTheDocument();
});

it("drops inaccessible first-page rows when the mounted list receives an authoritative refresh", () => {
  const view = renderWithMessages(
    <OrganizationList
      initialPage={{ items: [acme, beta], nextCursor: null }}
    />,
  );

  view.rerender(
    withMessages(
      <OrganizationList
        initialPage={{
          items: [{ ...acme, name: "Authoritative Acme" }],
          nextCursor: null,
        }}
      />,
    ),
  );

  expect(
    screen.getByRole("article", { name: "Authoritative Acme workspace" }),
  ).toBeVisible();
  expect(
    screen.queryByRole("article", { name: "Acme workspace" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("article", { name: "Beta workspace" }),
  ).not.toBeInTheDocument();
});

it("reconciles the authoritative first page while retaining only loaded continuation rows", async () => {
  getOrganizationPages.mockResolvedValue({
    data: { data: { items: [gamma], nextCursor: null } },
  } as Awaited<ReturnType<typeof getOrganizations>>);
  const view = renderWithMessages(
    <OrganizationList
      initialPage={{ items: [acme, beta], nextCursor: "opaque-next" }}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "Load more workspaces" }));
  await screen.findByRole("article", { name: "Gamma workspace" });

  view.rerender(
    withMessages(
      <OrganizationList
        initialPage={{
          items: [{ ...acme, name: "Authoritative Acme" }],
          nextCursor: "fresh-first-page-cursor",
        }}
      />,
    ),
  );

  expect(
    screen.getByRole("article", { name: "Authoritative Acme workspace" }),
  ).toBeVisible();
  expect(
    screen.queryByRole("article", { name: "Beta workspace" }),
  ).not.toBeInTheDocument();
  expect(
    screen.getByRole("article", { name: "Gamma workspace" }),
  ).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "Load more workspaces" }),
  ).not.toBeInTheDocument();
});

it("stops treating a continuation row as a tail after an authoritative first page adopts it", async () => {
  getOrganizationPages.mockResolvedValue({
    data: { data: { items: [beta], nextCursor: null } },
  } as Awaited<ReturnType<typeof getOrganizations>>);
  const view = renderWithMessages(
    <OrganizationList
      initialPage={{ items: [acme], nextCursor: "opaque-next" }}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "Load more workspaces" }));
  await screen.findByRole("article", { name: "Beta workspace" });

  view.rerender(
    withMessages(
      <OrganizationList
        initialPage={{
          items: [{ ...beta, name: "Authoritative Beta" }],
          nextCursor: null,
        }}
      />,
    ),
  );
  await waitFor(() => {
    expect(
      screen.queryByRole("article", { name: "Acme workspace" }),
    ).not.toBeInTheDocument();
  });
  expect(
    screen.getByRole("article", { name: "Authoritative Beta workspace" }),
  ).toBeVisible();

  view.rerender(
    withMessages(
      <OrganizationList initialPage={{ items: [], nextCursor: null }} />,
    ),
  );

  expect(
    screen.queryByRole("article", { name: "Authoritative Beta workspace" }),
  ).not.toBeInTheDocument();
  expect(
    screen.getByRole("heading", { name: "No workspaces yet" }),
  ).toBeVisible();
});

it("does not restore tail provenance when a delayed continuation resolves after page one adopts its row", async () => {
  const delayedContinuation =
    deferred<Awaited<ReturnType<typeof getOrganizations>>>();
  getOrganizationPages.mockImplementation(
    () => delayedContinuation.promise as never,
  );
  const view = renderWithMessages(
    <OrganizationList
      initialPage={{ items: [acme], nextCursor: "opaque-next" }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Load more workspaces" }));
  await waitFor(() => expect(getOrganizationPages).toHaveBeenCalledTimes(1));
  view.rerender(
    withMessages(
      <OrganizationList initialPage={{ items: [beta], nextCursor: null }} />,
    ),
  );
  await waitFor(() => {
    expect(
      screen.queryByRole("article", { name: "Acme workspace" }),
    ).not.toBeInTheDocument();
  });
  expect(screen.getByRole("article", { name: "Beta workspace" })).toBeVisible();

  await act(async () => {
    delayedContinuation.resolve({
      data: { data: { items: [beta], nextCursor: null } },
    } as Awaited<ReturnType<typeof getOrganizations>>);
  });
  expect(screen.getByRole("article", { name: "Beta workspace" })).toBeVisible();

  view.rerender(
    withMessages(
      <OrganizationList initialPage={{ items: [], nextCursor: null }} />,
    ),
  );

  expect(
    screen.queryByRole("article", { name: "Beta workspace" }),
  ).not.toBeInTheDocument();
  expect(
    screen.getByRole("heading", { name: "No workspaces yet" }),
  ).toBeVisible();
});

it("lets refreshed entries replace identity, role, and permission controls while retaining local tail pages", async () => {
  getOrganizationPages.mockResolvedValue({
    data: { data: { items: [beta], nextCursor: null } },
  } as Awaited<ReturnType<typeof getOrganizations>>);
  const view = renderWithMessages(
    <OrganizationList
      initialPage={{ items: [acme], nextCursor: "opaque-next" }}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "Load more workspaces" }));
  await screen.findByRole("article", { name: "Beta workspace" });
  expect(
    within(screen.getByRole("article", { name: "Acme workspace" })).getByRole(
      "button",
      { name: "Delete workspace" },
    ),
  ).toBeVisible();

  view.rerender(
    withMessages(
      <OrganizationList
        initialPage={{
          items: [
            {
              ...acme,
              name: "Acme Renamed",
              slug: "acme-renamed",
              canonicalKey: "acme-renamed",
              currentRole: "member",
              capabilities: {
                ...capabilities,
                canDeleteOrganization: false,
              },
            },
          ],
          nextCursor: null,
        }}
      />,
    ),
  );

  const refreshed = screen.getByRole("article", {
    name: "Acme Renamed workspace",
  });
  expect(within(refreshed).getByText("Role: Member")).toBeVisible();
  expect(within(refreshed).getByText("acme-renamed")).toBeVisible();
  expect(
    within(refreshed).queryByRole("button", { name: "Delete workspace" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("article", { name: "Acme workspace" }),
  ).not.toBeInTheDocument();
  expect(screen.getByRole("article", { name: "Beta workspace" })).toBeVisible();
});

it("fetches the next opaque cursor without changing or amplifying the canonical URL", async () => {
  getOrganizationPages.mockResolvedValue({
    data: { data: { items: [beta], nextCursor: null } },
  } as Awaited<ReturnType<typeof getOrganizations>>);
  renderWithMessages(
    <OrganizationList
      initialPage={{ items: [acme], nextCursor: "opaque next + / =" }}
    />,
  );

  expect(
    screen.queryByRole("link", { name: "Load more workspaces" }),
  ).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Load more workspaces" }));
  await waitFor(() => {
    expect(getOrganizationPages).toHaveBeenCalledWith({
      client: { id: "browser-client" },
      cache: "no-store",
      query: { cursor: "opaque next + / =" },
    });
  });
  expect(window.location.href).not.toContain("cursor=");
});

it("keeps appending and advancing through more than ten generated reads", async () => {
  for (let page = 1; page <= 11; page += 1) {
    getOrganizationPages.mockResolvedValueOnce({
      data: {
        data: {
          items: [
            {
              ...acme,
              id: `page-${page}-id`,
              name: `Page ${page}`,
              slug: `page-${page}`,
              canonicalKey: `page-${page}`,
            },
          ],
          nextCursor: page === 11 ? null : `cursor-${page + 1}`,
        },
      },
    } as Awaited<ReturnType<typeof getOrganizations>>);
  }
  renderWithMessages(
    <OrganizationList
      initialPage={{ items: [acme], nextCursor: "cursor-1" }}
    />,
  );

  for (let page = 1; page <= 11; page += 1) {
    const loadMore = screen.getByRole("button", {
      name: "Load more workspaces",
    });
    fireEvent.click(loadMore);
    await screen.findByRole("article", {
      name: `Page ${page} workspace`,
    });
  }

  await waitFor(() => {
    expect(screen.getAllByRole("article")).toHaveLength(12);
  });
  expect(
    screen.getByRole("article", { name: "Page 11 workspace" }),
  ).toBeVisible();
  expect(
    screen.queryByRole("link", { name: "Load more workspaces" }),
  ).not.toBeInTheDocument();
});

it("renders the zero-state creation surface", () => {
  renderWithMessages(
    <OrganizationList initialPage={{ items: [], nextCursor: null }} />,
  );

  expect(
    screen.getByRole("heading", { name: "No workspaces yet" }),
  ).toBeVisible();
  expect(
    screen.getByRole("button", { name: "Create New Workspace" }),
  ).toBeVisible();
  expect(document.querySelector('[data-slot="empty"]')).toBeInTheDocument();
});

it("preserves successful pages and the cursor beside a safe generated-read failure", async () => {
  getOrganizationPages.mockResolvedValue({
    data: undefined,
    error: {
      code: "internal_error",
      traceId: "trace-more",
    },
    response: { status: 500 } as Response,
  } as Awaited<ReturnType<typeof getOrganizations>>);
  renderWithMessages(
    <OrganizationList
      initialPage={{ items: [acme, beta], nextCursor: "opaque-next" }}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "Load more workspaces" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Some workspaces could not be loaded.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-more");
  expect(
    screen.getByRole("button", { name: "Load more workspaces" }),
  ).toBeEnabled();
  expect(screen.getAllByRole("article")).toHaveLength(2);
  expect(document.querySelector('[data-slot="alert"]')).toBeInTheDocument();
});
