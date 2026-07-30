import { screen, within } from "@testing-library/react";

import { OrganizationList } from "@/src/components/organizations/organization-list";
import type {
  OrganizationPageResponse,
  OrganizationSummaryResponse,
} from "@/src/lib/api/generated/types.gen";
import { renderWithMessages } from "@/test/support/render";

jest.mock("next/navigation", () => ({
  useRouter: () => ({ push: jest.fn(), refresh: jest.fn() }),
}));

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
} satisfies OrganizationSummaryResponse;

it("renders canonical dashboard/settings links without delete controls", () => {
  renderWithMessages(
    <OrganizationList pages={[{ items: [acme], nextCursor: null }]} />,
  );

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

it("appends pages and de-duplicates organizations by id", () => {
  const pages: OrganizationPageResponse[] = [
    { items: [acme], nextCursor: "opaque-first" },
    {
      items: [{ ...acme, name: "Duplicate should not render" }, beta],
      nextCursor: null,
    },
  ];

  renderWithMessages(
    <OrganizationList loadedCursors={["opaque-first"]} pages={pages} />,
  );

  expect(screen.getAllByRole("article")).toHaveLength(2);
  expect(screen.getByRole("article", { name: "Acme workspace" })).toBeVisible();
  expect(screen.getByRole("article", { name: "Beta workspace" })).toBeVisible();
  expect(
    screen.queryByText("Duplicate should not render"),
  ).not.toBeInTheDocument();
});

it("exposes the next opaque cursor through explicit load-more navigation", () => {
  renderWithMessages(
    <OrganizationList
      loadedCursors={["opaque prior + / ="]}
      pages={[{ items: [acme], nextCursor: "opaque next + / =" }]}
    />,
  );

  expect(
    screen.getByRole("button", { name: "Load more workspaces" }),
  ).toBeVisible();
  const cursorInputs = screen
    .getByRole("button", { name: "Load more workspaces" })
    .closest("form")
    ?.querySelectorAll('input[name="cursor"]');
  expect(
    Array.from(cursorInputs ?? []).map((input) => input.getAttribute("value")),
  ).toEqual(["opaque prior + / =", "opaque next + / ="]);
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
});
