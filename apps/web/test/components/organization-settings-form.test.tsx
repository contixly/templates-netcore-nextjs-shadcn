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
        capabilities: {
          canUpdateOrganization: false,
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
  expect(refresh).toHaveBeenCalledTimes(1);
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
  expect(refresh).toHaveBeenCalledTimes(1);
});

it("sends only changed domains from a stale two-admin baseline and refreshes the baseline from success", async () => {
  const authoritativeAfterDomains = {
    ...ownerOrganization,
    name: "Renamed By Another Admin",
    slug: "renamed-by-another-admin",
    canonicalKey: "renamed-by-another-admin",
    allowedEmailDomains: ["new.example.com"],
  };
  updateOrganization
    .mockResolvedValueOnce({
      ok: true,
      data: authoritativeAfterDomains,
    })
    .mockResolvedValueOnce({
      ok: true,
      data: {
        ...authoritativeAfterDomains,
        name: "My Follow-up Rename",
      },
    });
  renderWithMessages(
    <OrganizationSettingsForm initialOrganization={ownerOrganization} />,
  );

  fireEvent.change(screen.getByLabelText("Allowed Email Domains"), {
    target: { value: "new.example.com" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  await waitFor(() => {
    expect(updateOrganization).toHaveBeenNthCalledWith(
      1,
      { id: "browser-client" },
      ownerOrganization.id,
      { allowedEmailDomains: ["new.example.com"] },
    );
  });
  expect(screen.getByLabelText("Workspace Name")).toHaveValue(
    "Renamed By Another Admin",
  );
  expect(screen.getByLabelText("Workspace Slug")).toHaveValue(
    "renamed-by-another-admin",
  );

  fireEvent.change(screen.getByLabelText("Workspace Name"), {
    target: { value: "My Follow-up Rename" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  await waitFor(() => {
    expect(updateOrganization).toHaveBeenNthCalledWith(
      2,
      { id: "browser-client" },
      ownerOrganization.id,
      { name: "My Follow-up Rename" },
    );
  });
});

it("sends only a changed name and excludes unchanged slug and domains", async () => {
  updateOrganization.mockResolvedValue({
    ok: true,
    data: {
      ...ownerOrganization,
      name: "Name Only",
    },
  });
  renderWithMessages(
    <OrganizationSettingsForm initialOrganization={ownerOrganization} />,
  );

  fireEvent.change(screen.getByLabelText("Workspace Name"), {
    target: { value: " Name Only " },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  await waitFor(() => {
    expect(updateOrganization).toHaveBeenCalledWith(
      { id: "browser-client" },
      ownerOrganization.id,
      { name: "Name Only" },
    );
  });
});

it("keeps save as a stable no-op when normalized settings are unchanged", () => {
  renderWithMessages(
    <OrganizationSettingsForm initialOrganization={ownerOrganization} />,
  );

  expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  expect(updateOrganization).not.toHaveBeenCalled();
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

it.each([
  {
    label: "Workspace Name",
    value: "Invalid!",
    message: "Use 1–50 letters, numbers, spaces, hyphens, or underscores.",
  },
  {
    label: "Workspace Slug",
    value: "invalid slug",
    message:
      "Use 1–64 lowercase letters or numbers separated by single hyphens.",
  },
  {
    label: "Allowed Email Domains",
    value: "not-a-domain",
    message: "Enter valid exact email domains such as example.com.",
  },
])(
  "associates the $label client error only with its actual field",
  async ({ label, message, value }) => {
    renderWithMessages(
      <OrganizationSettingsForm initialOrganization={ownerOrganization} />,
    );
    const field = screen.getByLabelText(label);

    fireEvent.change(field, { target: { value } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    const error = await screen.findByRole("alert");
    expect(error).toHaveTextContent(message);
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(field).toHaveAttribute(
      "aria-describedby",
      expect.stringContaining(error.id),
    );
    expect(field.closest('[data-slot="field"]')).toHaveAttribute(
      "data-invalid",
      "true",
    );
    for (const otherLabel of [
      "Workspace Name",
      "Workspace Slug",
      "Allowed Email Domains",
    ].filter((candidate) => candidate !== label)) {
      expect(screen.getByLabelText(otherLabel)).not.toHaveAttribute(
        "aria-invalid",
        "true",
      );
    }
    expect(updateOrganization).not.toHaveBeenCalled();
  },
);

it("rejects an exact normalized D-format UUID-shaped slug before transport", async () => {
  renderWithMessages(
    <OrganizationSettingsForm initialOrganization={ownerOrganization} />,
  );
  const slug = screen.getByLabelText("Workspace Slug");

  fireEvent.change(slug, {
    target: { value: " 123E4567-E89B-12D3-A456-426614174000 " },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Use 1–64 lowercase letters or numbers separated by single hyphens.",
  );
  expect(slug).toHaveAttribute("aria-invalid", "true");
  expect(slug.closest('[data-slot="field"]')).toHaveAttribute(
    "data-invalid",
    "true",
  );
  expect(updateOrganization).not.toHaveBeenCalled();
});

it("preserves a non-UUID canonical slug containing hexadecimal hyphen segments", async () => {
  updateOrganization.mockResolvedValue({
    ok: true,
    data: {
      ...ownerOrganization,
      slug: "123e4567-e89b-12d3-a456-42661417400z",
      canonicalKey: "123e4567-e89b-12d3-a456-42661417400z",
    },
  });
  renderWithMessages(
    <OrganizationSettingsForm initialOrganization={ownerOrganization} />,
  );

  fireEvent.change(screen.getByLabelText("Workspace Slug"), {
    target: { value: "123e4567-e89b-12d3-a456-42661417400z" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  await waitFor(() => {
    expect(updateOrganization).toHaveBeenCalledWith(
      { id: "browser-client" },
      ownerOrganization.id,
      expect.objectContaining({
        slug: "123e4567-e89b-12d3-a456-42661417400z",
      }),
    );
  });
});

it("renders API-wide validation as a form alert without falsely marking a field", async () => {
  updateOrganization.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "validation_failed",
      status: 400,
    },
  });
  renderWithMessages(
    <OrganizationSettingsForm initialOrganization={ownerOrganization} />,
  );

  fireEvent.change(screen.getByLabelText("Workspace Slug"), {
    target: { value: "api-valid-but-rejected" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Check the workspace details and try again.",
  );
  expect(screen.getByLabelText("Workspace Name")).not.toHaveAttribute(
    "aria-invalid",
    "true",
  );
  expect(screen.getByLabelText("Workspace Slug")).not.toHaveAttribute(
    "aria-invalid",
    "true",
  );
  expect(screen.getByLabelText("Allowed Email Domains")).not.toHaveAttribute(
    "aria-invalid",
    "true",
  );
});
