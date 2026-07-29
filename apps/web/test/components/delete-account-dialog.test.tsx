import { fireEvent, screen, waitFor } from "@testing-library/react";

import { DeleteAccountDialog } from "@/src/components/account/delete-account-dialog";
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
