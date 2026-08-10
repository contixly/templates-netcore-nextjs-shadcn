import {
  act,
  fireEvent,
  screen,
  waitFor,
  within,
} from "@testing-library/react";

import { ApiKeyCreateDialog } from "@/src/features/api-keys/ui/api-key-create-dialog";
import { createBrowserApiKey } from "@/src/lib/api/api-keys/browser/api-key-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { Client } from "@/src/lib/api/generated/client";
import type { ApiResult } from "@/src/lib/api/result";
import type { ApiKeySecretResponse } from "@/src/lib/api/generated/types.gen";
import { apiKeySecret, deferred } from "@/test/components/api-keys/fixtures";
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

it("uses the exact organization-read-all default for an organization owner", async () => {
  createKey.mockResolvedValue({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });
  const owner = {
    kind: "organization",
    organizationId: "01900000-0000-7000-8000-000000000910",
    organizationKey: "acme",
    capabilities: { canManageApiKeys: true },
  } as const;
  renderWithMessages(
    <ApiKeyCreateDialog onConfirmed={jest.fn()} owner={owner} />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Create API key" }));
  const dialog = screen.getByRole("dialog");
  expect(
    within(dialog).getByRole("checkbox", { name: "All organization reads" }),
  ).toBeChecked();
  expect(
    within(dialog).getByRole("checkbox", { name: "Basic read" }),
  ).not.toBeChecked();

  fireEvent.change(within(dialog).getByLabelText("Name"), {
    target: { value: "Organization automation" },
  });
  fireEvent.click(
    within(dialog).getByRole("button", { name: "Create API key" }),
  );

  await waitFor(() => expect(createKey).toHaveBeenCalledTimes(1));
  expect(createKey).toHaveBeenCalledWith(client, owner, {
    name: "Organization automation",
    presetIds: ["organization-read-all"],
    expiresIn: "30d",
    rateLimitEnabled: true,
    rateLimitMax: 1000,
    rateLimitWindow: "1h",
  });
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

it("uses an immediate single-flight guard and ignores completion after unmount", async () => {
  const pending = deferred<ApiResult<ApiKeySecretResponse>>();
  const onConfirmed = jest.fn();
  createKey.mockReturnValue(pending.promise);
  const view = renderWithMessages(
    <ApiKeyCreateDialog
      onConfirmed={onConfirmed}
      owner={{ kind: "personal" }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Create API key" }));
  fireEvent.change(screen.getByLabelText("Name"), {
    target: { value: "CLI" },
  });
  const form = screen
    .getByRole("button", { name: "Create API key" })
    .closest("form")!;
  act(() => {
    form.dispatchEvent(
      new Event("submit", { bubbles: true, cancelable: true }),
    );
    form.dispatchEvent(
      new Event("submit", { bubbles: true, cancelable: true }),
    );
  });

  expect(createKey).toHaveBeenCalledTimes(1);
  view.unmount();
  await act(async () => {
    pending.resolve({ ok: true, data: apiKeySecret });
  });
  expect(onConfirmed).not.toHaveBeenCalled();
});

it("always sends a finite strict rate maximum after clearing then disabling the field", async () => {
  createKey.mockResolvedValue({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });
  renderWithMessages(
    <ApiKeyCreateDialog onConfirmed={jest.fn()} owner={{ kind: "personal" }} />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Create API key" }));
  fireEvent.change(screen.getByLabelText("Name"), {
    target: { value: "CLI" },
  });
  fireEvent.change(screen.getByLabelText("Maximum requests"), {
    target: { value: "" },
  });
  fireEvent.click(
    screen.getByRole("switch", { name: "Rate limiting enabled" }),
  );
  fireEvent.click(screen.getByRole("button", { name: "Create API key" }));

  await waitFor(() => expect(createKey).toHaveBeenCalledTimes(1));
  expect(createKey.mock.calls[0]?.[2]).toEqual({
    name: "CLI",
    presetIds: ["basic-read"],
    expiresIn: "30d",
    rateLimitEnabled: false,
    rateLimitMax: 1000,
    rateLimitWindow: "1h",
  });
  expect(Number.isFinite(createKey.mock.calls[0]?.[2].rateLimitMax)).toBe(true);
});

it("resets every fresh create flow to the approved personal defaults", async () => {
  createKey.mockResolvedValue({ ok: true, data: apiKeySecret });
  renderWithMessages(
    <ApiKeyCreateDialog onConfirmed={jest.fn()} owner={{ kind: "personal" }} />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Create API key" }));
  fireEvent.change(screen.getByLabelText("Name"), {
    target: { value: "Privileged automation" },
  });
  fireEvent.click(
    screen.getByRole("checkbox", { name: "All organization reads" }),
  );
  fireEvent.click(screen.getByRole("button", { name: "Create API key" }));
  const secret = await screen.findByRole("dialog", {
    name: "Save this API key now",
  });
  fireEvent.click(within(secret).getByRole("button", { name: "I saved it" }));

  fireEvent.click(screen.getByRole("button", { name: "Create API key" }));
  const fresh = screen.getByRole("dialog", { name: "Create API key" });
  expect(within(fresh).getByLabelText("Name")).toHaveValue("");
  expect(
    within(fresh).getByRole("checkbox", { name: "Basic read" }),
  ).toBeChecked();
  expect(
    within(fresh).getByRole("checkbox", { name: "All organization reads" }),
  ).not.toBeChecked();
  expect(
    within(fresh).getByRole("combobox", { name: "Expiry" }),
  ).toHaveTextContent("30 days");
  expect(
    within(fresh).getByRole("switch", { name: "Rate limiting enabled" }),
  ).toBeChecked();
  expect(within(fresh).getByLabelText("Maximum requests")).toHaveValue(1000);
  expect(
    within(fresh).getByRole("combobox", { name: "Rate-limit window" }),
  ).toHaveTextContent("1 hour");
});
