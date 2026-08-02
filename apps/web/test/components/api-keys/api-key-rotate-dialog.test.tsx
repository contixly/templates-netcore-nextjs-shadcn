import { fireEvent, screen, waitFor, within } from "@testing-library/react";

import { ApiKeyRotateDialog } from "@/src/components/api-keys/api-key-rotate-dialog";
import { rotateBrowserApiKey } from "@/src/lib/api/api-keys/browser/api-key-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { Client } from "@/src/lib/api/generated/client";
import { apiKey, apiKeySecret } from "@/test/components/api-keys/fixtures";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/api-keys/browser/api-key-mutations", () => ({
  rotateBrowserApiKey: jest.fn(),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: jest.fn(),
}));

const client = { role: "browser" } as unknown as Client;
const rotateKey = jest.mocked(rotateBrowserApiKey);

beforeEach(() => {
  jest.clearAllMocks();
  jest.mocked(createBrowserApiClient).mockReturnValue(client);
});

it("requires confirmation and reveals the replacement only after confirmed rotation", async () => {
  const onConfirmed = jest.fn();
  rotateKey.mockResolvedValue({ ok: true, data: apiKeySecret });
  const { key: _secret, ...safeKey } = apiKeySecret;
  void _secret;
  renderWithMessages(
    <ApiKeyRotateDialog
      apiKey={apiKey}
      onConfirmed={onConfirmed}
      owner={{ kind: "personal" }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Rotate" }));
  const confirmation = screen.getByRole("dialog", {
    name: "Rotate CLI integration?",
  });
  expect(screen.queryByText(apiKeySecret.key)).not.toBeInTheDocument();
  expect(rotateKey).not.toHaveBeenCalled();

  fireEvent.click(
    within(confirmation).getByRole("button", { name: "Rotate key" }),
  );

  const secret = await screen.findByRole("dialog", {
    name: "Save this API key now",
  });
  expect(rotateKey).toHaveBeenCalledWith(
    client,
    { kind: "personal" },
    apiKey.id,
  );
  expect(onConfirmed).toHaveBeenCalledWith(safeKey);
  expect(within(secret).getByText(apiKeySecret.key)).toBeVisible();
  fireEvent.click(within(secret).getByRole("button", { name: "I saved it" }));
  await waitFor(() => {
    expect(screen.queryByText(apiKeySecret.key)).not.toBeInTheDocument();
  });
});

it("localizes a stable Problem Details failure without exposing its detail", async () => {
  rotateKey.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "api_key_permission_denied",
      status: 403,
      traceId: "trace-rotate",
    },
  });
  renderWithMessages(
    <ApiKeyRotateDialog
      apiKey={apiKey}
      onConfirmed={jest.fn()}
      owner={{ kind: "personal" }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Rotate" }));
  fireEvent.click(screen.getByRole("button", { name: "Rotate key" }));

  expect(await screen.findByText(/do not have permission/)).toBeVisible();
  expect(screen.getByText("trace-rotate")).toBeVisible();
});

it("drops a mismatched rotate credential without reconciling or revealing it", async () => {
  const onConfirmed = jest.fn();
  const mismatched = { ...apiKeySecret, id: "different-key" };
  rotateKey.mockResolvedValue({ ok: true, data: mismatched });
  renderWithMessages(
    <ApiKeyRotateDialog
      apiKey={apiKey}
      onConfirmed={onConfirmed}
      owner={{ kind: "personal" }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Rotate" }));
  fireEvent.click(screen.getByRole("button", { name: "Rotate key" }));

  expect(
    await screen.findByText("The request could not be completed."),
  ).toBeVisible();
  expect(onConfirmed).not.toHaveBeenCalled();
  expect(screen.queryByText(apiKeySecret.key)).not.toBeInTheDocument();
  expect(mismatched.key).toBe("");
});
