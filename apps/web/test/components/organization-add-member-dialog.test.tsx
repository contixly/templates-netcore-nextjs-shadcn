import { fireEvent, screen, waitFor } from "@testing-library/react";

import { OrganizationAddMemberDialog } from "@/src/components/organizations/organization-add-member-dialog";
import { addBrowserOrganizationMember } from "@/src/lib/api/organizations/browser/organization-mutations";
import type { OrganizationMemberResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiResult } from "@/src/lib/api/result";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/organizations/browser/organization-mutations", () => ({
  addBrowserOrganizationMember: jest.fn(),
}));

const addMember = jest.mocked(addBrowserOrganizationMember);
const organizationId = "01900000-0000-7000-8000-000000000010";
const userId = "01900000-0000-7000-8000-000000000020";
const member = {
  id: "01900000-0000-7000-8000-000000000030",
  userId,
  name: "Outside User",
  email: "outside@external.test",
  imageUrl: null,
  role: "member",
  joinedAt: "2026-07-30T10:00:00Z",
  emailDomain: "external.test",
  isOutsideAllowedEmailDomains: true,
} satisfies OrganizationMemberResponse;

beforeEach(() => {
  jest.clearAllMocks();
});

async function openAndFill() {
  fireEvent.click(screen.getByRole("button", { name: "Add member" }));
  fireEvent.change(await screen.findByLabelText("User ID"), {
    target: { value: userId },
  });
}

it("accepts an exact UUID user id and prevents invalid ids from reaching the adapter", async () => {
  renderWithMessages(
    <OrganizationAddMemberDialog
      assignableRoles={["member", "admin", "owner"]}
      organizationId={organizationId}
      onMemberConfirmed={jest.fn()}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Add member" }));
  fireEvent.change(await screen.findByLabelText("User ID"), {
    target: { value: "not-a-user-id" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Add" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Enter an exact UUID user ID.",
  );
  expect(addMember).not.toHaveBeenCalled();
});

it("shows the API-provided domain warning and retries exactly once only after confirmation", async () => {
  const onMemberConfirmed = jest.fn();
  addMember
    .mockResolvedValueOnce({
      ok: false,
      failure: {
        kind: "problem",
        code: "member_domain_acknowledgement_required",
        status: 409,
        traceId: "trace-domain",
        email: "outside@external.test",
        emailDomain: "external.test",
        allowedEmailDomains: ["example.com", "team.example.com"],
      },
    })
    .mockResolvedValueOnce({ ok: true, data: member });
  renderWithMessages(
    <OrganizationAddMemberDialog
      assignableRoles={["member", "admin", "owner"]}
      organizationId={organizationId}
      onMemberConfirmed={onMemberConfirmed}
    />,
  );
  await openAndFill();

  fireEvent.click(screen.getByRole("button", { name: "Add" }));

  const warning = await screen.findByRole("alert");
  expect(warning).toHaveTextContent("Email domain outside policy");
  expect(warning).toHaveTextContent("outside@external.test");
  expect(warning).toHaveTextContent("example.com, team.example.com");
  expect(addMember).toHaveBeenCalledTimes(1);
  expect(addMember).toHaveBeenNthCalledWith(
    1,
    { id: "browser-client" },
    organizationId,
    { userId, role: "member" },
  );

  fireEvent.click(screen.getByRole("button", { name: "Confirm add" }));

  await waitFor(() => {
    expect(addMember).toHaveBeenCalledTimes(2);
  });
  expect(addMember).toHaveBeenNthCalledWith(
    2,
    { id: "browser-client" },
    organizationId,
    {
      userId,
      role: "member",
      acknowledgeDomainRestriction: true,
    },
  );
  expect(onMemberConfirmed).toHaveBeenCalledWith(member);
});

it("does not offer a second acknowledgement retry when the acknowledged request fails", async () => {
  const domainFailure = {
    ok: false,
    failure: {
      kind: "problem",
      code: "member_domain_acknowledgement_required",
      status: 409,
      email: "outside@external.test",
      emailDomain: "external.test",
      allowedEmailDomains: ["example.com"],
    },
  } satisfies ApiResult<OrganizationMemberResponse>;
  addMember
    .mockResolvedValueOnce(domainFailure)
    .mockResolvedValueOnce(domainFailure);
  renderWithMessages(
    <OrganizationAddMemberDialog
      assignableRoles={["member", "admin"]}
      organizationId={organizationId}
      onMemberConfirmed={jest.fn()}
    />,
  );
  await openAndFill();

  fireEvent.click(screen.getByRole("button", { name: "Add" }));
  fireEvent.click(await screen.findByRole("button", { name: "Confirm add" }));

  await waitFor(() => {
    expect(addMember).toHaveBeenCalledTimes(2);
  });
  expect(
    screen.queryByRole("button", { name: "Confirm add" }),
  ).not.toBeInTheDocument();
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "The member could not be added.",
  );
});

it("never offers owner when the caller supplies the admin assignment matrix", async () => {
  renderWithMessages(
    <OrganizationAddMemberDialog
      assignableRoles={["member", "admin"]}
      organizationId={organizationId}
      onMemberConfirmed={jest.fn()}
    />,
  );
  await openAndFill();

  fireEvent.click(screen.getByRole("combobox", { name: "Role" }));

  expect(screen.getByRole("option", { name: "Member" })).toBeVisible();
  expect(screen.getByRole("option", { name: "Administrator" })).toBeVisible();
  expect(
    screen.queryByRole("option", { name: "Owner" }),
  ).not.toBeInTheDocument();
});
