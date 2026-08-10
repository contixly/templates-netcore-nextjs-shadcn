import { act, fireEvent, screen, waitFor } from "@testing-library/react";
import { Activity, StrictMode } from "react";
import { renderToString } from "react-dom/server";

import { OrganizationOnboarding } from "@/src/features/organizations/ui/organization-onboarding";
import { OrganizationCreateDialog } from "@/src/features/organizations/ui/organization-create-dialog";
import { createBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import { renderWithMessages, withMessages } from "@/test/support/render";

const organizationControlReadyAttribute =
  "data-organization-control-interaction-ready";

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

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((next) => {
    resolve = next;
  });
  return { promise, resolve };
}

const createdOrganization = {
  id: "01900000-0000-7000-8000-000000000010",
  name: "Acme Team",
  slug: "acme-team",
  canonicalKey: "acme-team",
  createdAt: "2026-07-30T10:00:00Z",
  updatedAt: "2026-07-30T10:00:00Z",
  accessPrincipal: "user" as const,
  currentRole: "owner" as const,
  capabilities: {
    canUpdateOrganization: true,
    canDeleteOrganization: true,
    canAddMembers: true,
    canUpdateMemberRoles: true,
    canManageTeams: true,
    canManageInvitations: true,
    canManageApiKeys: true,
  },
  allowedEmailDomains: [],
};

beforeEach(() => {
  jest.clearAllMocks();
});

it("keeps the create trigger unavailable in server HTML until its client handler is attached", async () => {
  const serverMarkup = renderToString(withMessages(<OrganizationOnboarding />));
  const serverDocument = new DOMParser().parseFromString(
    serverMarkup,
    "text/html",
  );
  const serverTrigger = Array.from(
    serverDocument.querySelectorAll("button"),
  ).find((button) => button.textContent?.includes("Create Workspace"));

  expect(serverTrigger?.hasAttribute("disabled")).toBe(true);
  expect(serverTrigger?.getAttribute(organizationControlReadyAttribute)).toBe(
    null,
  );

  renderWithMessages(<OrganizationOnboarding />);
  const trigger = screen.getByRole("button", { name: "Create Workspace" });
  await waitFor(() => {
    expect(trigger).toHaveAttribute(organizationControlReadyAttribute, "true");
  });
  expect(trigger).toBeEnabled();

  fireEvent.click(trigger);
  expect(await screen.findByRole("dialog")).toBeVisible();
});

it("offers first-workspace creation and invitation review without a target-only account action", () => {
  renderWithMessages(
    <main id="main-content">
      <OrganizationOnboarding />
    </main>,
  );

  expect(
    screen.getByRole("heading", { name: "Create your first workspace" }),
  ).toBeVisible();
  expect(
    screen.queryByRole("link", { name: "Account settings" }),
  ).not.toBeInTheDocument();
  expect(
    screen.getByRole("link", { name: "Review Invitations" }),
  ).toHaveAttribute("href", "/user/invitations");
  expect(screen.getAllByRole("main")).toHaveLength(1);
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

  for (const unsupportedNumber of ["Ⅻ", "²"]) {
    fireEvent.change(input, { target: { value: unsupportedNumber } });
    fireEvent.click(screen.getByRole("button", { name: "Create" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Use letters, numbers, spaces, hyphens, or underscores.",
    );
  }
  expect(createOrganization).not.toHaveBeenCalled();
});

it("keeps the Field disabled marker in sync while creation is pending", async () => {
  createOrganization.mockReturnValue(new Promise(() => undefined));
  renderWithMessages(<OrganizationOnboarding />);

  fireEvent.click(screen.getByRole("button", { name: "Create Workspace" }));
  const input = await screen.findByRole("textbox", { name: "Workspace name" });
  fireEvent.change(input, { target: { value: "Acme" } });
  fireEvent.click(screen.getByRole("button", { name: "Create" }));

  expect(input).toBeDisabled();
  expect(input.closest('[data-slot="field"]')).toHaveAttribute(
    "data-disabled",
    "true",
  );
});

it("uses the returned canonical key and refreshes after successful creation", async () => {
  createOrganization.mockResolvedValue({
    ok: true,
    data: createdOrganization,
  });
  renderWithMessages(
    <StrictMode>
      <OrganizationOnboarding />
    </StrictMode>,
  );

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

it("notifies the shell before navigating to a newly created workspace", async () => {
  const onNavigate = jest.fn();
  createOrganization.mockResolvedValue({
    ok: true,
    data: createdOrganization,
  });
  renderWithMessages(<OrganizationCreateDialog onNavigate={onNavigate} />);

  fireEvent.click(screen.getByRole("button", { name: "Create New Workspace" }));
  fireEvent.change(
    await screen.findByRole("textbox", { name: "Workspace name" }),
    { target: { value: "Acme Team" } },
  );
  fireEvent.click(screen.getByRole("button", { name: "Create" }));

  await waitFor(() => expect(push).toHaveBeenCalled());
  expect(onNavigate).toHaveBeenCalledTimes(1);
  expect(onNavigate.mock.invocationCallOrder[0]).toBeLessThan(
    push.mock.invocationCallOrder[0]!,
  );
});

it("suppresses a successful create continuation after permanent deletion", async () => {
  const pendingCreate =
    deferred<Awaited<ReturnType<typeof createBrowserOrganization>>>();
  createOrganization.mockReturnValue(pendingCreate.promise);
  const view = renderWithMessages(<OrganizationOnboarding />);

  fireEvent.click(screen.getByRole("button", { name: "Create Workspace" }));
  fireEvent.change(
    await screen.findByRole("textbox", { name: "Workspace name" }),
    { target: { value: "Acme Team" } },
  );
  fireEvent.click(screen.getByRole("button", { name: "Create" }));
  await waitFor(() => expect(createOrganization).toHaveBeenCalledTimes(1));

  view.unmount();
  await act(async () => {
    pendingCreate.resolve({ ok: true, data: createdOrganization });
    await pendingCreate.promise;
  });

  expect(push).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();
});

it("settles an Activity-hidden create and defers one refresh without stale navigation", async () => {
  const pendingCreate =
    deferred<Awaited<ReturnType<typeof createBrowserOrganization>>>();
  createOrganization.mockReturnValue(pendingCreate.promise);
  const onboarding = <OrganizationOnboarding />;
  const view = renderWithMessages(
    <Activity mode="visible">{onboarding}</Activity>,
  );

  fireEvent.click(screen.getByRole("button", { name: "Create Workspace" }));
  fireEvent.change(
    await screen.findByRole("textbox", { name: "Workspace name" }),
    { target: { value: "Acme Team" } },
  );
  fireEvent.click(screen.getByRole("button", { name: "Create" }));
  await waitFor(() => expect(createOrganization).toHaveBeenCalledTimes(1));

  view.rerender(withMessages(<Activity mode="hidden">{onboarding}</Activity>));
  await act(async () => {
    pendingCreate.resolve({ ok: true, data: createdOrganization });
    await pendingCreate.promise;
  });
  expect(push).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();

  view.rerender(withMessages(<Activity mode="visible">{onboarding}</Activity>));
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "Create Workspace" }),
  ).toBeEnabled();
  expect(push).not.toHaveBeenCalled();
  expect(refresh).toHaveBeenCalledTimes(1);

  view.rerender(withMessages(<Activity mode="hidden">{onboarding}</Activity>));
  view.rerender(withMessages(<Activity mode="visible">{onboarding}</Activity>));
  expect(refresh).toHaveBeenCalledTimes(1);
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

  await screen.findByText("Choose a different workspace name.");
  expect(screen.getByRole("alert")).toHaveTextContent("trace-create");
  expect(
    screen.queryByText("organization_name_conflict"),
  ).not.toBeInTheDocument();
  expect(push).not.toHaveBeenCalled();
});

it("presents API validation_failed as authoritative field validation", async () => {
  createOrganization.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "validation_failed",
      status: 400,
      traceId: "trace-validation",
    },
  });
  renderWithMessages(<OrganizationOnboarding />);

  fireEvent.click(screen.getByRole("button", { name: "Create Workspace" }));
  const input = await screen.findByRole("textbox", { name: "Workspace name" });
  fireEvent.change(input, { target: { value: "Acme" } });
  fireEvent.click(screen.getByRole("button", { name: "Create" }));

  expect(
    await screen.findByText("Check the workspace name and try again."),
  ).toBeVisible();
  expect(input).toHaveAttribute("aria-invalid", "true");
  expect(
    screen.queryByText("The workspace could not be created."),
  ).not.toBeInTheDocument();
  expect(screen.getByRole("alert")).toHaveTextContent("trace-validation");
});
