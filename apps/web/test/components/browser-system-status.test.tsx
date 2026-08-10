import { fireEvent, screen, waitFor } from "@testing-library/react";

import { BrowserSystemStatus } from "@/src/features/application/ui/system/browser-system-status";
import { loadBrowserSystemStatus } from "@/src/lib/api/browser/load-browser-system-status";
import type { SystemStatusResult } from "@/src/lib/api/result";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/browser/load-browser-system-status", () => ({
  loadBrowserSystemStatus: jest.fn(),
}));

const mockLoadBrowserSystemStatus = jest.mocked(loadBrowserSystemStatus);
const browserSuccess: SystemStatusResult = {
  ok: true,
  data: {
    status: "ok",
    apiVersion: "1",
    timestamp: "2026-07-24T00:00:00Z",
    echo: "browser",
  },
};

beforeEach(() => {
  mockLoadBrowserSystemStatus.mockReset();
});

describe("BrowserSystemStatus", () => {
  it("moves from loading to success", async () => {
    mockLoadBrowserSystemStatus.mockResolvedValue(browserSuccess);

    renderWithMessages(<BrowserSystemStatus />);

    expect(screen.getByTestId("status-browser")).toHaveTextContent(
      "Checking API status",
    );
    await waitFor(() =>
      expect(screen.getByTestId("status-browser")).toHaveTextContent(
        "API is available",
      ),
    );
  });

  it("retries a safe failure and restores success", async () => {
    mockLoadBrowserSystemStatus
      .mockResolvedValueOnce({
        ok: false,
        failure: { kind: "network", code: "api_unavailable" },
      })
      .mockResolvedValueOnce(browserSuccess);

    renderWithMessages(<BrowserSystemStatus />);

    await screen.findByText("The API is unavailable.");
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));

    await screen.findByText("API is available");
    expect(mockLoadBrowserSystemStatus).toHaveBeenCalledTimes(2);
  });

  it("aborts an obsolete request on unmount", () => {
    let capturedSignal: AbortSignal | undefined;
    mockLoadBrowserSystemStatus.mockImplementation((signal) => {
      capturedSignal = signal;
      return new Promise<SystemStatusResult>(() => {});
    });

    const view = renderWithMessages(<BrowserSystemStatus />);
    view.unmount();

    expect(capturedSignal?.aborted).toBe(true);
  });
});
