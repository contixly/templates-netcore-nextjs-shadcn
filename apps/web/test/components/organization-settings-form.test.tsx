import { fireEvent, screen, waitFor } from "@testing-library/react";

import { OrganizationSettingsForm } from "@/src/components/organizations/organization-settings-form";
import { updateBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import type { OrganizationDetailResponse } from "@/src/lib/api/generated/types.gen";
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
  updateBrowserOrganization: jest.fn(),
}));

const updateOrganization = jest.mocked(updateBrowserOrganization);

const ownerOrganization = {
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

beforeEach(() => {
  jest.clearAllMocks();
});

it("renders member organization settings as read-only without a save action", () => {
  renderWithMessages(
    <OrganizationSettingsForm
      initialOrganization={{
        ...ownerOrganization,
        currentRole: "member",
        capabilities: {
          canUpdateOrganization: false,
          canDeleteOrganization: false,
          canAddMembers: false,
          canUpdateMemberRoles: false,
        },
      }}
    />,
  );

  expect(screen.getByLabelText("Workspace Name")).toBeDisabled();
  expect(screen.getByLabelText("Workspace Slug")).toBeDisabled();
  expect(screen.getByLabelText("Allowed Email Domains")).toBeDisabled();
  expect(
    screen.queryByRole("button", { name: "Save" }),
  ).not.toBeInTheDocument();
  expect(
    screen.getByText(
      "Only workspace administrators and owners can change these details.",
    ),
  ).toBeVisible();
});

it("previews normalized domains, sends the generated update request, and replaces a changed canonical URL", async () => {
  updateOrganization.mockResolvedValue({
    ok: true,
    data: {
      ...ownerOrganization,
      name: "Acme Updated",
      slug: "acme-new",
      canonicalKey: "acme-new",
      allowedEmailDomains: ["example.com", "team.example.com"],
    },
  });
  renderWithMessages(
    <OrganizationSettingsForm initialOrganization={ownerOrganization} />,
  );

  fireEvent.change(screen.getByLabelText("Workspace Name"), {
    target: { value: " Acme Updated " },
  });
  fireEvent.change(screen.getByLabelText("Workspace Slug"), {
    target: { value: " ACME-NEW " },
  });
  fireEvent.change(screen.getByLabelText("Allowed Email Domains"), {
    target: {
      value: " Example.COM,\n@example.com\n team.example.com ",
    },
  });

  expect(
    screen.getByText("Normalized domains: example.com, team.example.com"),
  ).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  await waitFor(() => {
    expect(updateOrganization).toHaveBeenCalledWith(
      { id: "browser-client" },
      ownerOrganization.id,
      {
        name: "Acme Updated",
        slug: "acme-new",
        allowedEmailDomains: ["example.com", "team.example.com"],
      },
    );
  });
  expect(replace).toHaveBeenCalledWith("/w/acme-new/settings/workspace");
  expect(refresh).not.toHaveBeenCalled();
  expect(screen.getByLabelText("Workspace Name")).toHaveValue("Acme Updated");
});

it("keeps confirmed returned settings when the canonical key is unchanged", async () => {
  updateOrganization.mockResolvedValue({
    ok: true,
    data: {
      ...ownerOrganization,
      name: "Confirmed Name",
      allowedEmailDomains: ["confirmed.test"],
    },
  });
  renderWithMessages(
    <OrganizationSettingsForm initialOrganization={ownerOrganization} />,
  );

  fireEvent.change(screen.getByLabelText("Workspace Name"), {
    target: { value: "Confirmed Name" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  expect(await screen.findByRole("status")).toHaveTextContent(
    "Workspace settings saved.",
  );
  expect(screen.getByLabelText("Workspace Name")).toHaveValue("Confirmed Name");
  expect(replace).not.toHaveBeenCalled();
});

it("uses stable localized update errors and exposes only an allowed trace id", async () => {
  updateOrganization.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "organization_slug_conflict",
      status: 409,
      traceId: "trace-settings",
    },
  });
  renderWithMessages(
    <OrganizationSettingsForm initialOrganization={ownerOrganization} />,
  );

  fireEvent.change(screen.getByLabelText("Workspace Slug"), {
    target: { value: "taken" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Choose a different workspace slug.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-settings");
  expect(screen.getByRole("alert")).not.toHaveTextContent(
    "organization_slug_conflict",
  );
});
