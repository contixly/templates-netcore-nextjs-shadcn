import { act, fireEvent, screen, waitFor } from "@testing-library/react";
import { Activity, StrictMode, useLayoutEffect } from "react";

import { OrganizationDeleteDialog } from "@/src/features/organizations/ui/organization-delete-dialog";
import { deleteBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import { renderWithMessages, withMessages } from "@/test/support/render";

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

function ActivityHideSignal({ onHidden }: Readonly<{ onHidden: () => void }>) {
  useLayoutEffect(() => () => onHidden(), [onHidden]);
  return null;
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((complete) => {
    resolve = complete;
  });
  return { promise, resolve };
}

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

it("completes a visible deletion once after StrictMode replays its lifecycle", async () => {
  const onDeleted = jest.fn();
  deleteOrganization.mockResolvedValue({
    ok: true,
    data: { organizationId: organization.id },
  });
  renderWithMessages(
    <StrictMode>
      <OrganizationDeleteDialog
        canDelete
        onDeleted={onDeleted}
        organization={organization}
      />
    </StrictMode>,
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
    expect(onDeleted).toHaveBeenCalledTimes(1);
    expect(replace).toHaveBeenCalledWith("/workspaces");
  });
  expect(deleteOrganization).toHaveBeenCalledTimes(1);
  expect(refresh).toHaveBeenCalledTimes(1);
});

it("settles an Activity-hidden success and routes exactly once on reveal", async () => {
  const hiddenDelete =
    deferred<Awaited<ReturnType<typeof deleteBrowserOrganization>>>();
  const onDeleted = jest.fn();
  const hidden = jest.fn();
  deleteOrganization.mockReturnValueOnce(hiddenDelete.promise);
  const dialog = (
    <>
      <OrganizationDeleteDialog
        canDelete
        onDeleted={onDeleted}
        organization={organization}
      />
      <ActivityHideSignal onHidden={hidden} />
    </>
  );
  const view = renderWithMessages(<Activity mode="visible">{dialog}</Activity>);

  fireEvent.click(screen.getByRole("button", { name: "Delete workspace" }));
  fireEvent.change(await screen.findByLabelText('Type "Acme" to confirm'), {
    target: { value: "Acme" },
  });
  fireEvent.click(
    screen.getByRole("button", {
      name: "Permanently delete workspace",
    }),
  );
  expect(
    await screen.findByRole("button", { name: "Deleting workspace" }),
  ).toBeDisabled();

  view.rerender(withMessages(<Activity mode="hidden">{dialog}</Activity>));
  expect(hidden).toHaveBeenCalledTimes(1);
  await act(async () => {
    hiddenDelete.resolve({
      ok: true,
      data: { organizationId: organization.id },
    });
    await hiddenDelete.promise;
  });

  expect(onDeleted).toHaveBeenCalledWith(organization.id);
  expect(onDeleted).toHaveBeenCalledTimes(1);
  expect(replace).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();

  view.rerender(withMessages(<Activity mode="visible">{dialog}</Activity>));
  await waitFor(() => expect(replace).toHaveBeenCalledWith("/workspaces"));
  expect(refresh).toHaveBeenCalledTimes(1);
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

  view.rerender(withMessages(<Activity mode="hidden">{dialog}</Activity>));
  view.rerender(withMessages(<Activity mode="visible">{dialog}</Activity>));
  expect(replace).toHaveBeenCalledTimes(1);
  expect(refresh).toHaveBeenCalledTimes(1);
});

it("discards a queued Activity-hidden deletion route when permanently deleted", async () => {
  const hiddenDelete =
    deferred<Awaited<ReturnType<typeof deleteBrowserOrganization>>>();
  const onDeleted = jest.fn();
  const hidden = jest.fn();
  deleteOrganization.mockReturnValueOnce(hiddenDelete.promise);
  const dialog = (
    <>
      <OrganizationDeleteDialog
        canDelete
        onDeleted={onDeleted}
        organization={organization}
      />
      <ActivityHideSignal onHidden={hidden} />
    </>
  );
  const view = renderWithMessages(<Activity mode="visible">{dialog}</Activity>);

  fireEvent.click(screen.getByRole("button", { name: "Delete workspace" }));
  fireEvent.change(await screen.findByLabelText('Type "Acme" to confirm'), {
    target: { value: "Acme" },
  });
  fireEvent.click(
    screen.getByRole("button", {
      name: "Permanently delete workspace",
    }),
  );
  await waitFor(() => expect(deleteOrganization).toHaveBeenCalledTimes(1));

  view.rerender(withMessages(<Activity mode="hidden">{dialog}</Activity>));
  expect(hidden).toHaveBeenCalledTimes(1);
  await act(async () => {
    hiddenDelete.resolve({
      ok: true,
      data: { organizationId: organization.id },
    });
    await hiddenDelete.promise;
  });
  expect(onDeleted).toHaveBeenCalledTimes(1);
  expect(replace).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();

  view.unmount();
  expect(replace).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();
});

it("reconciles an Activity-hidden failure without routing and allows retry", async () => {
  const hiddenDelete =
    deferred<Awaited<ReturnType<typeof deleteBrowserOrganization>>>();
  const hidden = jest.fn();
  deleteOrganization
    .mockReturnValueOnce(hiddenDelete.promise)
    .mockResolvedValueOnce({
      ok: true,
      data: { organizationId: organization.id },
    });
  const dialog = (
    <>
      <OrganizationDeleteDialog canDelete organization={organization} />
      <ActivityHideSignal onHidden={hidden} />
    </>
  );
  const view = renderWithMessages(<Activity mode="visible">{dialog}</Activity>);

  fireEvent.click(screen.getByRole("button", { name: "Delete workspace" }));
  fireEvent.change(await screen.findByLabelText('Type "Acme" to confirm'), {
    target: { value: "Acme" },
  });
  fireEvent.click(
    screen.getByRole("button", {
      name: "Permanently delete workspace",
    }),
  );
  view.rerender(withMessages(<Activity mode="hidden">{dialog}</Activity>));
  expect(hidden).toHaveBeenCalledTimes(1);
  await act(async () => {
    hiddenDelete.resolve({
      ok: false,
      failure: {
        kind: "problem",
        code: "concurrency_conflict",
        status: 409,
        traceId: "hidden-delete-failure",
      },
    });
    await hiddenDelete.promise;
  });
  expect(replace).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();

  view.rerender(withMessages(<Activity mode="visible">{dialog}</Activity>));
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "hidden-delete-failure",
  );
  expect(
    screen.getByRole("button", { name: "Permanently delete workspace" }),
  ).toBeEnabled();
  fireEvent.click(
    screen.getByRole("button", {
      name: "Permanently delete workspace",
    }),
  );

  await waitFor(() => expect(replace).toHaveBeenCalledWith("/workspaces"));
  expect(deleteOrganization).toHaveBeenCalledTimes(2);
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
