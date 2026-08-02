import { fireEvent, screen, within } from "@testing-library/react";

import { ApiKeyEditDialog } from "@/src/components/api-keys/api-key-edit-dialog";
import { updateBrowserApiKey } from "@/src/lib/api/api-keys/browser/api-key-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { Client } from "@/src/lib/api/generated/client";
import { apiKey } from "@/test/components/api-keys/fixtures";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/api-keys/browser/api-key-mutations", () => ({
  updateBrowserApiKey: jest.fn(),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: jest.fn(),
}));

const client = { role: "browser" } as unknown as Client;
const updateKey = jest.mocked(updateBrowserApiKey);

beforeEach(() => {
  jest.clearAllMocks();
  jest.mocked(createBrowserApiClient).mockReturnValue(client);
});

it("blocks a semantic no-op without calling the update mutation", () => {
  renderWithMessages(
    <ApiKeyEditDialog
      apiKey={apiKey}
      onConfirmed={jest.fn()}
      owner={{ kind: "personal" }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Edit" }));
  const dialog = screen.getByRole("dialog");
  fireEvent.click(within(dialog).getByRole("button", { name: "Save changes" }));

  expect(within(dialog).getByRole("alert")).toHaveTextContent(
    "Make at least one change before saving.",
  );
  expect(updateKey).not.toHaveBeenCalled();
});

it("sends only confirmed generated update members", async () => {
  const updated = { ...apiKey, name: "Build agent" };
  const onConfirmed = jest.fn();
  updateKey.mockResolvedValue({ ok: true, data: updated });
  renderWithMessages(
    <ApiKeyEditDialog
      apiKey={apiKey}
      onConfirmed={onConfirmed}
      owner={{ kind: "personal" }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Edit" }));
  fireEvent.change(screen.getByLabelText("Name"), {
    target: { value: " Build agent " },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save changes" }));

  expect(await screen.findByRole("button", { name: "Edit" })).toBeVisible();
  expect(updateKey).toHaveBeenCalledWith(
    client,
    { kind: "personal" },
    apiKey.id,
    { name: "Build agent" },
  );
  expect(onConfirmed).toHaveBeenCalledWith(updated);
});
