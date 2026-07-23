import { fireEvent, screen } from "@testing-library/react";

import {
  StatusCard,
  StatusCardSkeleton,
} from "@/src/components/system/status-card";
import { renderWithMessages } from "@/test/support/render";

const success = {
  status: "ok",
  apiVersion: "1",
  timestamp: "2026-07-24T00:00:00Z",
  echo: "browser",
};

describe("StatusCard", () => {
  it("renders generated success data in an accessible live region", () => {
    renderWithMessages(
      <StatusCard
        source="browser"
        state={{ kind: "success", data: success }}
      />,
    );

    const region = screen.getByTestId("status-browser");
    expect(region).toHaveAttribute("role", "status");
    expect(region).toHaveAttribute("aria-live", "polite");
    expect(region).toHaveTextContent("API is available");
    expect(region).toHaveTextContent("Browser");
    expect(region).toHaveTextContent("ok");
    expect(region).toHaveTextContent("browser");
  });

  it.each([
    [
      { kind: "problem", code: "internal_error", status: 500 },
      "The API could not complete the request.",
    ],
    [
      { kind: "problem", code: "unknown_code", status: 400 },
      "The API returned an error.",
    ],
    [{ kind: "network", code: "api_unavailable" }, "The API is unavailable."],
    [
      { kind: "configuration", code: "api_configuration_missing" },
      "The server API address is not configured.",
    ],
  ] as const)("renders safe failure %p", (failure, expected) => {
    renderWithMessages(
      <StatusCard source="ssr" state={{ kind: "failure", failure }} />,
    );

    expect(screen.getByTestId("status-ssr")).toHaveTextContent(expected);
  });

  it("shows trace ID and delegates retry without raw details", () => {
    const onRetry = jest.fn();

    renderWithMessages(
      <StatusCard
        onRetry={onRetry}
        source="browser"
        state={{
          kind: "failure",
          failure: {
            kind: "problem",
            code: "validation_failed",
            status: 400,
            traceId: "trace-safe",
          },
        }}
      />,
    );

    expect(screen.getByText("Trace ID: trace-safe")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it("renders a labelled skeleton", () => {
    renderWithMessages(
      <StatusCardSkeleton
        label="Checking API status"
        source="ssr"
        title="Server-rendered API status"
      />,
    );

    expect(screen.getByTestId("status-ssr")).toHaveAttribute(
      "aria-label",
      "Checking API status",
    );
  });
});
