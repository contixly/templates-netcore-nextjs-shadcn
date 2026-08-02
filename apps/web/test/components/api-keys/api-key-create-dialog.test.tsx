import { fireEvent, screen, waitFor, within } from "@testing-library/react";

import { ApiKeyCreateDialog } from "@/src/components/api-keys/api-key-create-dialog";
import { createBrowserApiKey } from "@/src/lib/api/api-keys/browser/api-key-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { Client } from "@/src/lib/api/generated/client";
import { apiKeySecret } from "@/test/components/api-keys/fixtures";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/api-keys/browser/api-key-mutations", () => ({
  createBrowserApiKey: jest.fn(),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: jest.fn(),
}));

const client = { role: "browser" } as unknown as Client;
const createKey = jest.mocked(createBrowserApiKey);
const createClient = jest.mocked(createBrowserApiClient);

beforeEach(() => {
  jest.clearAllMocks();
  createClient.mockReturnValue(client);
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    value: { writeText: jest.fn().mockResolvedValue(undefined) },
  });
});

it("uses approved personal defaults and rejects invalid input before the API boundary", () => {
  renderWithMessages(
    <ApiKeyCreateDialog onConfirmed={jest.fn()} owner={{ kind: "personal" }} />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Create API key" }));
  const dialog = screen.getByRole("dialog");
  expect(
    within(dialog).getByRole("checkbox", { name: "Basic read" }),
  ).toBeChecked();
  expect(
    within(dialog).getByRole("combobox", { name: "Expiry" }),
  ).toHaveTextContent("30 days");
  expect(
    within(dialog).getByRole("switch", { name: "Rate limiting enabled" }),
  ).toBeChecked();
  expect(within(dialog).getByLabelText("Maximum requests")).toHaveValue(1000);
  expect(
    within(dialog).getByRole("combobox", { name: "Rate-limit window" }),
  ).toHaveTextContent("1 hour");

  fireEvent.click(
    within(dialog).getByRole("button", { name: "Create API key" }),
  );

  expect(within(dialog).getByRole("alert")).toHaveTextContent("Enter a name.");
  expect(createKey).not.toHaveBeenCalled();
});

it("submits the generated request and limits the raw credential to explicit reveal and copy", async () => {
  const onConfirmed = jest.fn();
  createKey.mockResolvedValue({ ok: true, data: apiKeySecret });
  const { key: _secret, ...safeKey } = apiKeySecret;
  void _secret;
  renderWithMessages(
    <ApiKeyCreateDialog
      onConfirmed={onConfirmed}
      owner={{ kind: "personal" }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Create API key" }));
  fireEvent.change(screen.getByLabelText("Name"), {
    target: { value: "  CLI integration  " },
  });
  fireEvent.click(screen.getByRole("button", { name: "Create API key" }));

  const secretDialog = await screen.findByRole("dialog", {
    name: "Save this API key now",
  });
  expect(createKey).toHaveBeenCalledWith(
    client,
    { kind: "personal" },
    {
      name: "CLI integration",
      presetIds: ["basic-read"],
      expiresIn: "30d",
      rateLimitEnabled: true,
      rateLimitMax: 1000,
      rateLimitWindow: "1h",
    },
  );
  expect(onConfirmed).toHaveBeenCalledWith(safeKey);
  expect(within(secretDialog).getByText(apiKeySecret.key)).toBeVisible();
  expect(navigator.clipboard.writeText).not.toHaveBeenCalled();

  fireEvent.click(
    within(secretDialog).getByRole("button", { name: "Copy credential" }),
  );
  await waitFor(() => {
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(
      apiKeySecret.key,
    );
  });
  fireEvent.click(
    within(secretDialog).getByRole("button", { name: "I saved it" }),
  );
  await waitFor(() => {
    expect(screen.queryByText(apiKeySecret.key)).not.toBeInTheDocument();
  });
});
