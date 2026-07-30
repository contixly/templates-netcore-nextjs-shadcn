import { fireEvent, screen, waitFor } from "@testing-library/react";

import { OrganizationOnboarding } from "@/src/components/organizations/organization-onboarding";
import { createBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import { renderWithMessages } from "@/test/support/render";

const push = jest.fn();
const refresh = jest.fn();

jest.mock("next/navigation", () => ({
  useRouter: () => ({ push, refresh }),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/organizations/browser/organization-mutations", () => ({
  createBrowserOrganization: jest.fn(),
}));

const createOrganization = jest.mocked(createBrowserOrganization);

beforeEach(() => {
  jest.clearAllMocks();
});

it("offers first-workspace creation and account settings without invitations", () => {
  renderWithMessages(<OrganizationOnboarding />);

  expect(
    screen.getByRole("heading", { name: "Create your first workspace" }),
  ).toBeVisible();
  expect(
    screen.getByRole("link", { name: "Account settings" }),
  ).toHaveAttribute("href", "/user/profile");
  expect(
    screen.queryByRole("link", { name: /invitation/i }),
  ).not.toBeInTheDocument();
});

it("validates the trimmed UTF-16 name and supported characters before mutation", async () => {
  renderWithMessages(<OrganizationOnboarding />);

  fireEvent.click(screen.getByRole("button", { name: "Create Workspace" }));
  const input = await screen.findByRole("textbox", {
    name: "Workspace name",
  });
  fireEvent.change(input, { target: { value: "Acme!" } });
  fireEvent.click(screen.getByRole("button", { name: "Create" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Use letters, numbers, spaces, hyphens, or underscores.",
  );
  expect(createOrganization).not.toHaveBeenCalled();

  fireEvent.change(input, { target: { value: "a".repeat(51) } });
  fireEvent.click(screen.getByRole("button", { name: "Create" }));
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Use 50 characters or fewer.",
  );
  expect(createOrganization).not.toHaveBeenCalled();
});

it("uses the returned canonical key and refreshes after successful creation", async () => {
  createOrganization.mockResolvedValue({
    ok: true,
    data: {
      id: "01900000-0000-7000-8000-000000000010",
      name: "Acme Team",
      slug: "acme-team",
      canonicalKey: "acme-team",
      createdAt: "2026-07-30T10:00:00Z",
      updatedAt: "2026-07-30T10:00:00Z",
      currentRole: "owner",
      capabilities: {
        canUpdateOrganization: true,
        canDeleteOrganization: true,
        canAddMembers: true,
        canUpdateMemberRoles: true,
      },
      allowedEmailDomains: [],
    },
  });
  renderWithMessages(<OrganizationOnboarding />);

  fireEvent.click(screen.getByRole("button", { name: "Create Workspace" }));
  fireEvent.change(
    await screen.findByRole("textbox", { name: "Workspace name" }),
    { target: { value: "  Acme Team  " } },
  );
  fireEvent.click(screen.getByRole("button", { name: "Create" }));

  await waitFor(() => {
    expect(createOrganization).toHaveBeenCalledWith(
      { id: "browser-client" },
      { name: "Acme Team" },
    );
    expect(push).toHaveBeenCalledWith("/w/acme-team/dashboard");
    expect(refresh).toHaveBeenCalledTimes(1);
  });
});

it("shows stable API failure copy and trace without raw problem codes", async () => {
  createOrganization.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "organization_name_conflict",
      status: 409,
      traceId: "trace-create",
    },
  });
  renderWithMessages(<OrganizationOnboarding />);

  fireEvent.click(screen.getByRole("button", { name: "Create Workspace" }));
  fireEvent.change(
    await screen.findByRole("textbox", { name: "Workspace name" }),
    { target: { value: "Acme" } },
  );
  fireEvent.click(screen.getByRole("button", { name: "Create" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Choose a different workspace name.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-create");
  expect(
    screen.queryByText("organization_name_conflict"),
  ).not.toBeInTheDocument();
  expect(push).not.toHaveBeenCalled();
});
