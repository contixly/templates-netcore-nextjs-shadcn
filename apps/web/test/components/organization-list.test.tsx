import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";

import { OrganizationList } from "@/src/components/organizations/organization-list";
import { deleteBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import type {
  OrganizationPageResponse,
  OrganizationSummaryResponse,
} from "@/src/lib/api/generated/types.gen";
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

const deleteOrganization = jest.mocked(deleteBrowserOrganization);

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

it("renders canonical dashboard/settings links without delete controls", () => {
  renderWithMessages(
    <OrganizationList pages={[{ items: [acme], nextCursor: null }]} />,
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
    <OrganizationList pages={[{ items: [acme, beta], nextCursor: null }]} />,
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
      pages={[{ items: [acme, otherOwner], nextCursor: null }]}
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
        pages={[{ items: [acme, otherOwner], nextCursor: null }]}
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
      <OrganizationList pages={[{ items: [acme, beta], nextCursor: null }]} />
    </NextIntlClientProvider>,
  );

  expect(screen.getByText("Роль: Владелец")).toBeVisible();
  expect(screen.getByText("Роль: Участник")).toBeVisible();
  expect(screen.queryByText("Роль: owner")).not.toBeInTheDocument();
  expect(screen.queryByText("Роль: member")).not.toBeInTheDocument();
});

it("appends pages and de-duplicates organizations by id", () => {
  const pages: OrganizationPageResponse[] = [
    { items: [acme], nextCursor: "opaque-first" },
    {
      items: [{ ...acme, name: "Duplicate should not render" }, beta],
      nextCursor: null,
    },
  ];

  renderWithMessages(<OrganizationList pages={pages} />);

  expect(screen.getAllByRole("article")).toHaveLength(2);
  expect(screen.getByRole("article", { name: "Acme workspace" })).toBeVisible();
  expect(screen.getByRole("article", { name: "Beta workspace" })).toBeVisible();
  expect(
    screen.queryByText("Duplicate should not render"),
  ).not.toBeInTheDocument();
});

it("exposes only the next opaque cursor through explicit load-more navigation", () => {
  renderWithMessages(
    <OrganizationList
      pages={[{ items: [acme], nextCursor: "opaque next + / =" }]}
    />,
  );

  expect(
    screen.getByRole("link", { name: "Load more workspaces" }),
  ).toHaveAttribute(
    "href",
    "/workspaces?cursor=opaque%20next%20%2B%20%2F%20%3D",
  );
});

it("keeps appending and advancing through more than ten soft navigations", async () => {
  const view = renderWithMessages(
    <OrganizationList pages={[{ items: [acme], nextCursor: "cursor-1" }]} />,
  );

  for (let page = 1; page <= 11; page += 1) {
    const loadMore = screen.getByRole("link", {
      name: "Load more workspaces",
    });
    expect(loadMore).toHaveAttribute(
      "href",
      `/workspaces?cursor=cursor-${page}`,
    );
    fireEvent.click(loadMore);
    const organization = {
      ...acme,
      id: `page-${page}-id`,
      name: `Page ${page}`,
      slug: `page-${page}`,
      canonicalKey: `page-${page}`,
    };
    view.rerender(
      withMessages(
        <OrganizationList
          pages={[
            { items: [acme], nextCursor: "cursor-1" },
            {
              items: [organization],
              nextCursor: page === 11 ? null : `cursor-${page + 1}`,
            },
          ]}
        />,
      ),
    );
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
    <OrganizationList pages={[{ items: [], nextCursor: null }]} />,
  );

  expect(
    screen.getByRole("heading", { name: "No workspaces yet" }),
  ).toBeVisible();
  expect(
    screen.getByRole("button", { name: "Create New Workspace" }),
  ).toBeVisible();
  expect(document.querySelector('[data-slot="empty"]')).toBeInTheDocument();
});

it("preserves successful continuation pages beside a stable partial failure", () => {
  renderWithMessages(
    <OrganizationList
      continuationFailure={{
        kind: "problem",
        code: "internal_error",
        status: 500,
        traceId: "trace-more",
      }}
      pages={[
        { items: [acme], nextCursor: "opaque-first" },
        { items: [beta], nextCursor: "opaque-next" },
      ]}
    />,
  );

  expect(screen.getAllByRole("article")).toHaveLength(2);
  expect(screen.getByRole("alert")).toHaveTextContent(
    "Some workspaces could not be loaded.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-more");
  expect(
    screen.queryByRole("button", { name: "Load more workspaces" }),
  ).not.toBeInTheDocument();
  expect(document.querySelector('[data-slot="alert"]')).toBeInTheDocument();
});
