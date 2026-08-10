import { act, fireEvent, screen, waitFor } from "@testing-library/react";

import { DeleteAccountDialog } from "@/src/features/account/ui/delete-account-dialog";
import { deleteBrowserAccount } from "@/src/lib/api/account/browser/account-mutations";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/account/browser/account-mutations", () => ({
  deleteBrowserAccount: jest.fn(),
}));

const deleteAccount = jest.mocked(deleteBrowserAccount);

type LocationImplementation = {
  assign(url: string): void;
};

function locationImplementation(): LocationImplementation {
  const implementationSymbol = Object.getOwnPropertySymbols(
    window.location,
  ).find((symbol) => symbol.description === "impl");

  if (!implementationSymbol) {
    throw new Error("JSDOM location implementation is unavailable.");
  }

  return (window.location as unknown as Record<symbol, LocationImplementation>)[
    implementationSymbol
  ];
}

let assignSpy: jest.SpiedFunction<LocationImplementation["assign"]>;

beforeEach(() => {
  jest.clearAllMocks();
  assignSpy = jest
    .spyOn(locationImplementation(), "assign")
    .mockImplementation(() => undefined);
});

afterEach(() => {
  assignSpy.mockRestore();
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise;
  });
  return { promise, resolve };
}

it("requires the exact primary email before enabling deletion", async () => {
  renderWithMessages(
    <DeleteAccountDialog primaryEmail="Account@Example.test" />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Delete account" }));
  const input = await screen.findByRole("textbox", {
    name: /Type Account@Example\.test to confirm/,
  });
  const confirm = screen.getByRole("button", {
    name: "Permanently delete account",
  });

  expect(confirm).toBeDisabled();
  fireEvent.change(input, { target: { value: "account@example.test" } });
  expect(confirm).toBeDisabled();
  fireEvent.change(input, { target: { value: " Account@Example.test " } });
  expect(confirm).toBeEnabled();
});

it("moves focus into the dialog and closes with Escape", async () => {
  renderWithMessages(
    <DeleteAccountDialog primaryEmail="account@example.test" />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Delete account" }));
  const input = await screen.findByRole("textbox", {
    name: /Type account@example\.test to confirm/,
  });
  await waitFor(() => {
    expect(input).toHaveFocus();
  });

  fireEvent.keyDown(document, { key: "Escape" });
  await waitFor(() => {
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
  expect(screen.getByRole("button", { name: "Delete account" })).toHaveFocus();
});

it("blocks dismissal and duplicate submission while deletion is in flight", async () => {
  const request = deferred<Awaited<ReturnType<typeof deleteBrowserAccount>>>();
  deleteAccount.mockReturnValueOnce(request.promise).mockResolvedValueOnce({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });
  renderWithMessages(
    <DeleteAccountDialog primaryEmail="account@example.test" />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Delete account" }));
  const input = await screen.findByRole("textbox", {
    name: /Type account@example\.test to confirm/,
  });
  fireEvent.change(input, { target: { value: "account@example.test" } });
  const form = input.closest("form");
  if (!form) {
    throw new Error("Delete form is unavailable.");
  }

  act(() => {
    form.dispatchEvent(
      new Event("submit", { bubbles: true, cancelable: true }),
    );
    form.dispatchEvent(
      new Event("submit", { bubbles: true, cancelable: true }),
    );
  });

  await waitFor(() => {
    expect(deleteAccount).toHaveBeenCalledTimes(1);
  });
  expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled();

  const overlay = document.querySelector('[data-slot="dialog-overlay"]');
  if (!(overlay instanceof HTMLElement)) {
    throw new Error("Delete dialog overlay is unavailable.");
  }

  fireEvent.keyDown(document, { key: "Escape" });
  fireEvent.pointerDown(overlay);
  fireEvent.click(overlay);
  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

  expect(screen.getByRole("dialog")).toBeInTheDocument();
  expect(deleteAccount).toHaveBeenCalledTimes(1);

  await act(async () => {
    request.resolve({
      ok: false,
      failure: {
        kind: "problem",
        code: "validation_failed",
        status: 400,
        traceId: "trace-deferred-delete",
      },
    });
  });

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "trace-deferred-delete",
  );
  expect(input).toHaveValue("account@example.test");
  expect(screen.getByRole("button", { name: "Cancel" })).toBeEnabled();

  fireEvent.keyDown(document, { key: "Escape" });
  await waitFor(() => {
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  fireEvent.click(screen.getByRole("button", { name: "Delete account" }));
  fireEvent.change(
    await screen.findByRole("textbox", {
      name: /Type account@example\.test to confirm/,
    }),
    { target: { value: "account@example.test" } },
  );
  fireEvent.click(
    screen.getByRole("button", { name: "Permanently delete account" }),
  );

  await waitFor(() => {
    expect(deleteAccount).toHaveBeenCalledTimes(2);
  });
});

it("deletes through the account adapter then performs a full home reload", async () => {
  deleteAccount.mockResolvedValue({
    ok: true,
    data: { deleted: true },
  });
  renderWithMessages(
    <DeleteAccountDialog primaryEmail="account@example.test" />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Delete account" }));
  fireEvent.change(
    await screen.findByRole("textbox", {
      name: /Type account@example\.test to confirm/,
    }),
    { target: { value: " account@example.test " } },
  );
  fireEvent.click(
    screen.getByRole("button", { name: "Permanently delete account" }),
  );

  await waitFor(() => {
    expect(deleteAccount).toHaveBeenCalledWith(
      { id: "browser-client" },
      { confirmationEmail: "account@example.test" },
    );
    expect(assignSpy).toHaveBeenCalledWith("/");
  });
});

it("keeps the dialog recoverable after a server failure", async () => {
  deleteAccount.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "validation_failed",
      status: 400,
      traceId: "trace-delete",
    },
  });
  renderWithMessages(
    <DeleteAccountDialog primaryEmail="account@example.test" />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Delete account" }));
  fireEvent.change(
    await screen.findByRole("textbox", {
      name: /Type account@example\.test to confirm/,
    }),
    { target: { value: "account@example.test" } },
  );
  const confirm = screen.getByRole("button", {
    name: "Permanently delete account",
  });
  fireEvent.click(confirm);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "The account could not be deleted.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-delete");
  expect(screen.getByRole("dialog")).toBeInTheDocument();
  expect(confirm).toBeEnabled();
  expect(assignSpy).not.toHaveBeenCalled();
});

it("explains the exact organization ownership blocker without hiding its trace", async () => {
  deleteAccount.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "organization_ownership_transfer_required",
      status: 409,
      traceId: "trace-ownership-blocker",
    },
  });
  renderWithMessages(
    <DeleteAccountDialog primaryEmail="account@example.test" />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Delete account" }));
  fireEvent.change(
    await screen.findByRole("textbox", {
      name: /Type account@example\.test to confirm/,
    }),
    { target: { value: "account@example.test" } },
  );
  fireEvent.click(
    screen.getByRole("button", { name: "Permanently delete account" }),
  );

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Promote another member to owner or share ownership, then try deleting your account again.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent(
    "trace-ownership-blocker",
  );
  expect(screen.getByRole("dialog")).toBeInTheDocument();
  expect(assignSpy).not.toHaveBeenCalled();
});
