import { screen } from "@testing-library/react";
import { connection } from "next/server";

import { ServerSystemStatus } from "@/src/components/system/server-system-status";
import { loadServerSystemStatus } from "@/src/lib/api/server/load-server-system-status";
import { renderWithMessages } from "@/test/support/render";

jest.mock("next/server", () => ({
  connection: jest.fn(),
}));
jest.mock("@/src/lib/api/server/load-server-system-status", () => ({
  loadServerSystemStatus: jest.fn(),
}));

const mockConnection = jest.mocked(connection);
const mockLoadServerSystemStatus = jest.mocked(loadServerSystemStatus);

beforeEach(() => {
  mockConnection.mockResolvedValue(undefined);
  mockLoadServerSystemStatus.mockReset();
});

describe("ServerSystemStatus", () => {
  it("waits for a request before loading the SSR status", async () => {
    mockLoadServerSystemStatus.mockResolvedValue({
      ok: true,
      data: {
        status: "ok",
        apiVersion: "1",
        timestamp: "2026-07-24T00:00:00Z",
        echo: "ssr",
      },
    });

    renderWithMessages(await ServerSystemStatus());

    expect(mockConnection).toHaveBeenCalledTimes(1);
    expect(mockLoadServerSystemStatus).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("status-ssr")).toHaveTextContent("ssr");
  });

  it("keeps expected configuration failures inside the status region", async () => {
    mockLoadServerSystemStatus.mockResolvedValue({
      ok: false,
      failure: {
        kind: "configuration",
        code: "api_configuration_missing",
      },
    });

    renderWithMessages(await ServerSystemStatus());

    expect(screen.getByTestId("status-ssr")).toHaveTextContent(
      "The server API address is not configured.",
    );
  });
});
