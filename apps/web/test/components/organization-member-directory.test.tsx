import { fireEvent, screen, waitFor, within } from "@testing-library/react";

import { OrganizationMemberDirectory } from "@/src/components/organizations/organization-member-directory";
import { updateBrowserOrganizationMemberRole } from "@/src/lib/api/organizations/browser/organization-mutations";
import { getOrganizationMembers } from "@/src/lib/api/generated/sdk.gen";
import type {
  OrganizationDetailResponse,
  OrganizationMemberPageResponse,
  OrganizationMemberResponse,
} from "@/src/lib/api/generated/types.gen";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/organizations/browser/organization-mutations", () => ({
  addBrowserOrganizationMember: jest.fn(),
  updateBrowserOrganizationMemberRole: jest.fn(),
}));
jest.mock("@/src/lib/api/generated/sdk.gen", () => ({
  getOrganizationMembers: jest.fn(),
}));

const getMembers = jest.mocked(getOrganizationMembers);
const updateRole = jest.mocked(updateBrowserOrganizationMemberRole);
const currentUserId = "01900000-0000-7000-8000-000000000020";
const organization = {
  id: "01900000-0000-7000-8000-000000000010",
  name: "Acme",
  slug: "acme",
  canonicalKey: "acme",
  createdAt: "2026-07-30T10:00:00Z",
  updatedAt: "2026-07-30T10:00:00Z",
  currentRole: "owner",
  capabilities: {
    canUpdateOrganization: true,
    canDeleteOrganization: true,
    canAddMembers: true,
    canUpdateMemberRoles: true,
  },
  allowedEmailDomains: ["example.com"],
} satisfies OrganizationDetailResponse;
const currentMember = {
  id: "01900000-0000-7000-8000-000000000030",
  userId: currentUserId,
  name: "Current User",
  email: "current@example.com",
  imageUrl: null,
  role: "owner",
  joinedAt: "2026-07-29T10:00:00Z",
  emailDomain: "example.com",
  isOutsideAllowedEmailDomains: false,
} satisfies OrganizationMemberResponse;
const otherMember = {
  id: "01900000-0000-7000-8000-000000000031",
  userId: "01900000-0000-7000-8000-000000000021",
  name: "Other User",
  email: "other@external.test",
  imageUrl: null,
  role: "member",
  joinedAt: "2026-07-30T10:00:00Z",
  emailDomain: "external.test",
  isOutsideAllowedEmailDomains: true,
} satisfies OrganizationMemberResponse;
const initialPage = {
  items: [currentMember, otherMember],
  nextCursor: "cursor-next",
} satisfies OrganizationMemberPageResponse;

beforeEach(() => {
  jest.clearAllMocks();
});

it("separates the current actor, preserves returned order, and never renders member removal", () => {
  const laterMember = {
    ...otherMember,
    id: "01900000-0000-7000-8000-000000000032",
    userId: "01900000-0000-7000-8000-000000000022",
    name: "Later User",
    email: "later@example.com",
    isOutsideAllowedEmailDomains: false,
  };
  renderWithMessages(
    <OrganizationMemberDirectory
      currentUserId={currentUserId}
      initialPage={{
        items: [currentMember, otherMember, laterMember],
        nextCursor: null,
      }}
      organization={organization}
    />,
  );

  const ownAccess = screen.getByRole("region", { name: "Your access" });
  expect(within(ownAccess).getByText("Current User")).toBeVisible();
  expect(within(ownAccess).queryByRole("combobox")).not.toBeInTheDocument();

  const others = screen.getByRole("region", { name: "Other members" });
  const rows = within(others).getAllByRole("article");
  expect(rows[0]).toHaveTextContent("Other User");
  expect(rows[1]).toHaveTextContent("Later User");
  expect(within(others).queryByText("Current User")).not.toBeInTheDocument();
  expect(screen.getByText("Outside domain policy")).toBeVisible();
  expect(
    screen.queryByRole("button", { name: /remove|delete member/i }),
  ).not.toBeInTheDocument();
});

it("does not offer owner assignment or owner mutation to an admin", () => {
  renderWithMessages(
    <OrganizationMemberDirectory
      currentUserId={currentUserId}
      initialPage={{
        items: [
          { ...currentMember, role: "admin" },
          otherMember,
          {
            ...otherMember,
            id: "owner-member",
            userId: "owner-user",
            name: "Workspace Owner",
            role: "owner",
          },
        ],
        nextCursor: null,
      }}
      organization={{ ...organization, currentRole: "admin" }}
    />,
  );

  expect(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  ).toBeVisible();
  expect(
    screen.queryByRole("combobox", { name: "Role for Workspace Owner" }),
  ).not.toBeInTheDocument();

  fireEvent.click(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  );
  expect(
    screen.queryByRole("option", { name: "Owner" }),
  ).not.toBeInTheDocument();
});

it("loads the next opaque cursor, appends members, and deduplicates ids", async () => {
  const nextMember = {
    ...otherMember,
    id: "01900000-0000-7000-8000-000000000032",
    userId: "01900000-0000-7000-8000-000000000022",
    name: "Next User",
  };
  getMembers.mockResolvedValue({
    data: {
      data: {
        items: [otherMember, nextMember],
        nextCursor: null,
      },
    },
  } as Awaited<ReturnType<typeof getOrganizationMembers>>);
  renderWithMessages(
    <OrganizationMemberDirectory
      currentUserId={currentUserId}
      initialPage={initialPage}
      organization={organization}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Load more members" }));

  await waitFor(() => {
    expect(getMembers).toHaveBeenCalledWith({
      client: { id: "browser-client" },
      cache: "no-store",
      path: { organizationId: organization.id },
      query: { cursor: "cursor-next" },
    });
  });
  await waitFor(() => {
    expect(screen.getAllByRole("article")).toHaveLength(2);
  });
  expect(screen.getByText("Next User")).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "Load more members" }),
  ).not.toBeInTheDocument();
});

it("retains a confirmed role when refresh fails and retry performs GET only", async () => {
  const confirmed = { ...otherMember, role: "admin" as const };
  updateRole.mockResolvedValue({ ok: true, data: confirmed });
  getMembers
    .mockResolvedValueOnce({
      error: {
        code: "internal_error",
        traceId: "trace-members-refresh",
      },
      response: { status: 500 } as Response,
    } as Awaited<ReturnType<typeof getOrganizationMembers>>)
    .mockResolvedValueOnce({
      data: {
        data: {
          items: [currentMember, confirmed],
          nextCursor: null,
        },
      },
    } as Awaited<ReturnType<typeof getOrganizationMembers>>);
  renderWithMessages(
    <OrganizationMemberDirectory
      currentUserId={currentUserId}
      initialPage={{ ...initialPage, nextCursor: null }}
      organization={organization}
    />,
  );

  fireEvent.click(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  );
  fireEvent.click(screen.getByRole("option", { name: "Administrator" }));

  await waitFor(() => {
    expect(updateRole).toHaveBeenCalledTimes(1);
  });
  const refreshFailure = (
    await screen.findByText(
      "The member change was saved, but the directory could not be refreshed.",
    )
  ).closest('[role="alert"]');
  expect(refreshFailure).toHaveTextContent("trace-members-refresh");
  expect(
    screen.getByRole("combobox", { name: "Role for Other User" }),
  ).toHaveTextContent("Administrator");

  fireEvent.click(
    screen.getByRole("button", { name: "Retry member directory refresh" }),
  );

  expect(await screen.findByRole("status")).toHaveTextContent(
    "Member role updated.",
  );
  expect(getMembers).toHaveBeenCalledTimes(2);
  expect(updateRole).toHaveBeenCalledTimes(1);
  expect(getMembers).toHaveBeenNthCalledWith(2, {
    client: { id: "browser-client" },
    cache: "no-store",
    path: { organizationId: organization.id },
  });
});
