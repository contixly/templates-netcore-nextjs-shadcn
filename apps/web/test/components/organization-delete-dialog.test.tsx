import { fireEvent, screen, waitFor } from "@testing-library/react";

import { OrganizationDeleteDialog } from "@/src/components/organizations/organization-delete-dialog";
import { deleteBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import { renderWithMessages } from "@/test/support/render";

const replace = jest.fn();
const refresh = jest.fn();

jest.mock("next/navigation", () => ({
  useRouter: () => ({ replace, refresh }),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/organizations/browser/organization-mutations", () => ({
  deleteBrowserOrganization: jest.fn(),
}));

const deleteOrganization = jest.mocked(deleteBrowserOrganization);
const organization = {
  id: "01900000-0000-7000-8000-000000000010",
  name: "Acme",
};

beforeEach(() => {
  jest.clearAllMocks();
});

it("is absent when the server capability or accessible-organization gate denies deletion", () => {
  renderWithMessages(
    <OrganizationDeleteDialog canDelete={false} organization={organization} />,
  );
  expect(
    screen.queryByRole("button", { name: "Delete workspace" }),
  ).not.toBeInTheDocument();
  expect(deleteOrganization).not.toHaveBeenCalled();
});

it("requires an exact case-sensitive name without trimming", async () => {
  renderWithMessages(
    <OrganizationDeleteDialog canDelete organization={organization} />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Delete workspace" }));
  const confirmation = await screen.findByLabelText('Type "Acme" to confirm');
  const submit = screen.getByRole("button", {
    name: "Permanently delete workspace",
  });

  expect(submit).toBeDisabled();
  fireEvent.change(confirmation, { target: { value: "acme" } });
  expect(submit).toBeDisabled();
  fireEvent.change(confirmation, { target: { value: " Acme " } });
  expect(submit).toBeDisabled();
  fireEvent.change(confirmation, { target: { value: "Acme" } });
  expect(submit).toBeEnabled();
});

it("deletes through the Task 9 adapter and replaces the route with workspaces", async () => {
  deleteOrganization.mockResolvedValue({
    ok: true,
    data: { organizationId: organization.id },
  });
  renderWithMessages(
    <OrganizationDeleteDialog canDelete organization={organization} />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Delete workspace" }));
  fireEvent.change(await screen.findByLabelText('Type "Acme" to confirm'), {
    target: { value: "Acme" },
  });
  fireEvent.click(
    screen.getByRole("button", {
      name: "Permanently delete workspace",
    }),
  );

  await waitFor(() => {
    expect(deleteOrganization).toHaveBeenCalledWith(
      { id: "browser-client" },
      organization.id,
      { confirmationName: "Acme" },
    );
  });
  expect(replace).toHaveBeenCalledWith("/workspaces");
  expect(refresh).toHaveBeenCalledTimes(1);
});

it("keeps the confirmation dialog recoverable after a safe API failure", async () => {
  deleteOrganization.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "last_organization_required",
      status: 409,
      traceId: "trace-delete-workspace",
    },
  });
  renderWithMessages(
    <OrganizationDeleteDialog canDelete organization={organization} />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Delete workspace" }));
  fireEvent.change(await screen.findByLabelText('Type "Acme" to confirm'), {
    target: { value: "Acme" },
  });
  fireEvent.click(
    screen.getByRole("button", {
      name: "Permanently delete workspace",
    }),
  );

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Keep at least one accessible workspace.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-delete-workspace");
  expect(screen.getByRole("dialog")).toBeInTheDocument();
  expect(replace).not.toHaveBeenCalled();
});
