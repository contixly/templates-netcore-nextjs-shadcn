import {
  act,
  fireEvent,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { renderToString } from "react-dom/server";

import { ApiKeyManagement } from "@/src/components/api-keys/api-key-management";
import {
  listBrowserApiKeys,
  revokeBrowserApiKey,
  rotateBrowserApiKey,
  updateBrowserApiKey,
} from "@/src/lib/api/api-keys/browser/api-key-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { ApiResult } from "@/src/lib/api/result";
import type { ApiKeyPageResponse } from "@/src/lib/api/generated/types.gen";
import type { Client } from "@/src/lib/api/generated/client";
import {
  apiKey,
  apiKeyPage,
  apiKeySecret,
  deferred,
} from "@/test/components/api-keys/fixtures";
import { renderWithMessages, withMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/api-keys/browser/api-key-mutations", () => ({
  createBrowserApiKey: jest.fn(),
  listBrowserApiKeys: jest.fn(),
  revokeBrowserApiKey: jest.fn(),
  rotateBrowserApiKey: jest.fn(),
  updateBrowserApiKey: jest.fn(),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: jest.fn(),
}));

const client = { role: "browser" } as unknown as Client;
const listKeys = jest.mocked(listBrowserApiKeys);
const revokeKey = jest.mocked(revokeBrowserApiKey);
const rotateKey = jest.mocked(rotateBrowserApiKey);
const updateKey = jest.mocked(updateBrowserApiKey);

beforeEach(() => {
  listKeys.mockReset();
  revokeKey.mockReset();
  rotateKey.mockReset();
  updateKey.mockReset();
  jest.mocked(createBrowserApiClient).mockReset();
  jest.mocked(createBrowserApiClient).mockReturnValue(client);
});

it("renders education and an empty state", () => {
  renderWithMessages(
    <ApiKeyManagement
      initialPage={{ items: [], nextCursor: null }}
      owner={{ kind: "personal" }}
    />,
  );
  expect(screen.getByText("Choose the right owner")).toBeVisible();
  expect(screen.getByText("No API keys yet")).toBeVisible();
});

it("renders all safe fields without placing a raw credential in the table", () => {
  renderWithMessages(
    <ApiKeyManagement initialPage={apiKeyPage} owner={{ kind: "personal" }} />,
  );

  const row = screen.getByRole("row", { name: /CLI integration/ });
  expect(row).toHaveTextContent("tmpl_live_safe1");
  expect(row).toHaveTextContent("Active");
  expect(row).toHaveTextContent("Basic account read");
  expect(row).toHaveTextContent("1,000 per 1 hour");
  expect(row).toHaveTextContent("Sep 1, 2026");
  expect(row).toHaveTextContent("Aug 2, 2026");
  expect(row).not.toHaveTextContent(apiKeySecret.key);
});

it("keeps every server-rendered interaction unavailable until hydration", () => {
  const markup = renderToString(
    withMessages(
      <ApiKeyManagement
        initialPage={{ ...apiKeyPage, nextCursor: "next" }}
        owner={{ kind: "personal" }}
      />,
    ),
  );
  const document = new DOMParser().parseFromString(markup, "text/html");
  const labels = [
    "Create API key",
    "Edit",
    "Disable",
    "Rotate",
    "Revoke",
    "Load more API keys",
  ];

  for (const label of labels) {
    const button = [...document.querySelectorAll("button")].find(
      (candidate) => candidate.textContent === label,
    );
    expect(button).toBeDefined();
    expect(button?.hasAttribute("disabled")).toBe(true);
    expect(button?.getAttribute("data-interaction-ready")).toBe("false");
  }
});

it("deduplicates continuation pages by key ID", async () => {
  const second = {
    ...apiKey,
    id: "01900000-0000-7000-8000-000000000902",
    name: "Deploy",
  };
  listKeys.mockResolvedValue({
    ok: true,
    data: {
      items: [{ ...apiKey, name: "CLI refreshed" }, second],
      nextCursor: null,
    },
  });
  renderWithMessages(
    <ApiKeyManagement
      initialPage={{ ...apiKeyPage, nextCursor: "next" }}
      owner={{ kind: "personal" }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Load more API keys" }));
  await screen.findByText("CLI refreshed");

  expect(screen.getAllByRole("row")).toHaveLength(3);
  expect(listKeys).toHaveBeenCalledWith(
    client,
    { kind: "personal" },
    { cursor: "next" },
  );
});

it("retries a failed continuation with only another safe GET", async () => {
  listKeys
    .mockResolvedValueOnce({
      ok: false,
      failure: { kind: "network", code: "api_unavailable" },
    })
    .mockResolvedValueOnce({ ok: true, data: { items: [], nextCursor: null } });
  renderWithMessages(
    <ApiKeyManagement
      initialPage={{ ...apiKeyPage, nextCursor: "next" }}
      owner={{ kind: "personal" }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Load more API keys" }));
  expect(
    await screen.findByText("More API keys could not be loaded."),
  ).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "Retry list refresh" }));
  await waitFor(() => expect(listKeys).toHaveBeenCalledTimes(2));
  expect(updateKey).not.toHaveBeenCalled();
  expect(rotateKey).not.toHaveBeenCalled();
  expect(revokeKey).not.toHaveBeenCalled();
});

it("rejects stale GET completion and keeps a confirmed mutation over an older read", async () => {
  const staleRead = deferred<ApiResult<ApiKeyPageResponse>>();
  const refreshRead = deferred<ApiResult<ApiKeyPageResponse>>();
  listKeys
    .mockReturnValueOnce(staleRead.promise)
    .mockReturnValueOnce(refreshRead.promise);
  const disabled = { ...apiKey, status: "disabled" as const, enabled: false };
  updateKey.mockResolvedValue({ ok: true, data: disabled });
  renderWithMessages(
    <ApiKeyManagement
      initialPage={{ ...apiKeyPage, nextCursor: "next" }}
      owner={{ kind: "personal" }}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Load more API keys" }));
  fireEvent.click(screen.getByRole("button", { name: "Disable" }));
  await waitFor(() => expect(listKeys).toHaveBeenCalledTimes(2));

  const freshSecond = {
    ...apiKey,
    id: "01900000-0000-7000-8000-000000000902",
    name: "Fresh continuation",
  };
  await act(async () => {
    refreshRead.resolve({
      ok: true,
      data: { items: [disabled, freshSecond], nextCursor: null },
    });
  });
  expect(screen.getByText("Disabled")).toBeVisible();
  expect(screen.getByText("Fresh continuation")).toBeVisible();

  await act(async () => {
    staleRead.resolve({
      ok: true,
      data: {
        items: [apiKey, { ...freshSecond, name: "Stale continuation" }],
        nextCursor: null,
      },
    });
  });
  expect(screen.getByText("Disabled")).toBeVisible();
  expect(screen.queryByText("Stale continuation")).not.toBeInTheDocument();
});

it("removes a confirmed revoke immediately", async () => {
  revokeKey.mockResolvedValue({
    ok: true,
    data: { id: apiKey.id, revokedAt: "2026-08-02T11:00:00Z" },
  });
  listKeys.mockResolvedValue({
    ok: true,
    data: { items: [], nextCursor: null },
  });
  renderWithMessages(
    <ApiKeyManagement initialPage={apiKeyPage} owner={{ kind: "personal" }} />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Revoke" }));
  fireEvent.click(
    within(
      screen.getByRole("dialog", { name: "Revoke CLI integration?" }),
    ).getByRole("button", { name: "Revoke key" }),
  );
  await waitFor(() => {
    expect(
      screen.queryByRole("row", { name: /CLI integration/ }),
    ).not.toBeInTheDocument();
  });
});

it("keeps a rotate secret through failed refresh and retries only the safe GET", async () => {
  rotateKey.mockResolvedValue({ ok: true, data: apiKeySecret });
  listKeys
    .mockResolvedValueOnce({
      ok: false,
      failure: { kind: "network", code: "api_unavailable" },
    })
    .mockResolvedValueOnce({
      ok: true,
      data: { items: [], nextCursor: null },
    });
  renderWithMessages(
    <ApiKeyManagement initialPage={apiKeyPage} owner={{ kind: "personal" }} />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Rotate" }));
  fireEvent.click(
    within(
      screen.getByRole("dialog", { name: "Rotate CLI integration?" }),
    ).getByRole("button", { name: "Rotate key" }),
  );
  expect(await screen.findByText(apiKeySecret.key)).toBeVisible();
  expect(await screen.findByText(/change succeeded/)).toBeVisible();

  fireEvent.click(screen.getByText("Retry list refresh").closest("button")!);
  await waitFor(() => expect(listKeys).toHaveBeenCalledTimes(2));
  expect(rotateKey).toHaveBeenCalledTimes(1);
  expect(screen.getByText(apiKeySecret.key)).toBeVisible();
});
