import { fireEvent, screen, within } from "@testing-library/react";

import { DeleteAccountDialog } from "@/src/features/account/ui/delete-account-dialog";
import { renderWithMessages } from "@/test/support/render";

test("dangerous account dialog exposes the reference destructive action hierarchy", async () => {
  renderWithMessages(
    <DeleteAccountDialog primaryEmail="account@example.test" />,
  );
  fireEvent.click(screen.getByRole("button", { name: "Delete account" }));
  const dialog = await screen.findByRole("dialog");
  expect(dialog).toHaveClass("min-w-md");
  expect(
    screen.getByRole("button", { name: "Permanently delete account" }),
  ).toHaveClass("bg-destructive");
  expect(screen.getByRole("button", { name: "Cancel" })).toHaveClass("border");
  expect(within(dialog).getByText("Warning")).toBeVisible();
  expect(
    within(dialog)
      .getAllByRole("listitem")
      .map((item) => item.textContent),
  ).toEqual([
    "All your data will be permanently deleted",
    "Your connected accounts will be unlinked",
    "Your active sessions will be terminated",
    "This action cannot be reversed",
  ]);
});
