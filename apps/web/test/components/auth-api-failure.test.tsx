import { screen } from "@testing-library/react";

import { AuthApiFailure } from "@/src/components/authentication/auth-api-failure";
import type { ApiFailure } from "@/src/lib/api/result";
import { renderWithMessages } from "@/test/support/render";

it("renders only generic localized copy and the trace ID", () => {
  const failure = {
    kind: "problem",
    code: "internal_error",
    status: 500,
    traceId: "trace-safe",
    detail: "sensitive database detail",
  } as ApiFailure;

  renderWithMessages(<AuthApiFailure failure={failure} />);

  expect(
    screen.getByRole("heading", { name: "Authentication is unavailable" }),
  ).toBeInTheDocument();
  expect(screen.getByText("Try again later.")).toBeInTheDocument();
  expect(screen.getByText("trace-safe")).toBeInTheDocument();
  expect(
    screen.queryByText("sensitive database detail"),
  ).not.toBeInTheDocument();
});
