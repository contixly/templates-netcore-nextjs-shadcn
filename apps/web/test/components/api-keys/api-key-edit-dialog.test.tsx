import {
  act,
  fireEvent,
  screen,
  waitFor,
  within,
} from "@testing-library/react";

import { ApiKeyEditDialog } from "@/src/features/api-keys/ui/api-key-edit-dialog";
import { updateBrowserApiKey } from "@/src/lib/api/api-keys/browser/api-key-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { Client } from "@/src/lib/api/generated/client";
import type { ApiKeyResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiResult } from "@/src/lib/api/result";
import { apiKey, deferred } from "@/test/components/api-keys/fixtures";
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

it("guards duplicate edit submissions synchronously and ignores an unmounted completion", async () => {
  const pending = deferred<ApiResult<ApiKeyResponse>>();
  const onConfirmed = jest.fn();
  updateKey.mockReturnValue(pending.promise);
  const view = renderWithMessages(
    <ApiKeyEditDialog
      apiKey={apiKey}
      onConfirmed={onConfirmed}
      owner={{ kind: "personal" }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Edit" }));
  fireEvent.change(screen.getByLabelText("Name"), {
    target: { value: "Build agent" },
  });
  const form = screen
    .getByRole("button", { name: "Save changes" })
    .closest("form")!;
  act(() => {
    form.dispatchEvent(
      new Event("submit", { bubbles: true, cancelable: true }),
    );
    form.dispatchEvent(
      new Event("submit", { bubbles: true, cancelable: true }),
    );
  });

  expect(updateKey).toHaveBeenCalledTimes(1);
  view.unmount();
  await act(async () => {
    pending.resolve({ ok: true, data: { ...apiKey, name: "Build agent" } });
  });
  expect(onConfirmed).not.toHaveBeenCalled();
});

it("rejects an update response for a different key as a safe localized failure", async () => {
  const onConfirmed = jest.fn();
  updateKey.mockResolvedValue({
    ok: true,
    data: { ...apiKey, id: "different-key", name: "Build agent" },
  });
  renderWithMessages(
    <ApiKeyEditDialog
      apiKey={apiKey}
      onConfirmed={onConfirmed}
      owner={{ kind: "personal" }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Edit" }));
  fireEvent.change(screen.getByLabelText("Name"), {
    target: { value: "Build agent" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save changes" }));

  expect(
    await screen.findByText("The request could not be completed."),
  ).toBeVisible();
  expect(onConfirmed).not.toHaveBeenCalled();
  expect(
    screen.getByRole("dialog", { name: "Edit CLI integration" }),
  ).toBeVisible();
});

it("omits an invalid hidden rate maximum when disabling rate limiting", async () => {
  updateKey.mockResolvedValue({
    ok: true,
    data: { ...apiKey, rateLimitEnabled: false },
  });
  renderWithMessages(
    <ApiKeyEditDialog
      apiKey={apiKey}
      onConfirmed={jest.fn()}
      owner={{ kind: "personal" }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Edit" }));
  fireEvent.change(screen.getByLabelText("Maximum requests"), {
    target: { value: "" },
  });
  fireEvent.click(
    screen.getByRole("switch", { name: "Rate limiting enabled" }),
  );
  fireEvent.click(screen.getByRole("button", { name: "Save changes" }));

  await waitFor(() => expect(updateKey).toHaveBeenCalledTimes(1));
  expect(updateKey.mock.calls[0]?.[3]).toEqual({ rateLimitEnabled: false });
  expect(JSON.stringify(updateKey.mock.calls[0]?.[3])).not.toContain("null");
});
