import { fireEvent, render, screen } from "@testing-library/react";
import { renderToStaticMarkup } from "react-dom/server";

import RouteError from "@/src/app/error";
import Forbidden from "@/src/app/forbidden";
import GlobalError from "@/src/app/global-error";
import Loading from "@/src/app/loading";
import NotFound from "@/src/app/not-found";
import Unauthorized from "@/src/app/unauthorized";
import { renderWithMessages } from "@/test/support/render";

jest.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => {
    const values: Record<string, string> = {
      "system.boundaries.loading": "Loading page",
      "system.boundaries.notFoundTitle": "Page not found",
      "system.boundaries.notFoundDescription":
        "The requested route does not exist.",
      "application.shell.safeBoundaries.loadingTitle": "Loading application",
      "application.shell.safeBoundaries.forbiddenTitle": "Access denied",
      "application.shell.safeBoundaries.forbiddenDescription":
        "You do not have permission to open this page.",
      "application.shell.safeBoundaries.unauthorizedTitle": "Sign in required",
      "application.shell.safeBoundaries.unauthorizedDescription":
        "Sign in to continue to this page.",
      "application.shell.safeBoundaries.unauthorizedAction": "Sign in",
      "application.shell.safeBoundaries.notFoundTitle": "Page not found",
      "application.shell.safeBoundaries.notFoundDescription":
        "The page you requested is unavailable or you do not have access to it.",
      "system.status.loading": "Checking API status",
      "system.status.ssrTitle": "Server-rendered API status",
      "system.status.browserTitle": "Browser API status",
      "common.actions.home": "Return home",
    };

    return (key: string) => values[`${namespace}.${key}`] ?? key;
  },
}));

describe("Next boundaries", () => {
  it("renders an accessible localized loading state", async () => {
    render(await Loading());

    expect(
      screen.getByRole("heading", { name: "Loading application" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveAttribute("aria-busy", "true");
    expect(screen.queryByText(/API/iu)).not.toBeInTheDocument();
  });

  it("renders route error safely and calls reset", () => {
    const reset = jest.fn();

    renderWithMessages(
      <RouteError error={new Error("private-route-error")} reset={reset} />,
    );

    expect(screen.queryByText("private-route-error")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(reset).toHaveBeenCalledTimes(1);
  });

  it("renders not-found with a root link", async () => {
    render(await NotFound());

    expect(
      screen.getByRole("heading", { name: "Page not found" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Return home" })).toHaveAttribute(
      "href",
      "/",
    );
  });

  it("renders localized unauthorized copy with a safe sign-in link", async () => {
    render(await Unauthorized());

    expect(
      screen.getByRole("heading", { name: "Sign in required" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Sign in to continue to this page."),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute(
      "href",
      "/auth/login",
    );
  });

  it("renders localized forbidden copy without disclosing a protected resource", async () => {
    render(await Forbidden());

    expect(
      screen.getByRole("heading", { name: "Access denied" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("You do not have permission to open this page."),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Return home" })).toHaveAttribute(
      "href",
      "/",
    );
  });

  it("renders global error without any provider and hides raw errors", () => {
    const reset = jest.fn();

    const markup = renderToStaticMarkup(
      <GlobalError error={new Error("private-global-error")} reset={reset} />,
    );

    expect(markup).toContain("Application error");
    expect(markup).toContain("Reload application");
    expect(markup).not.toContain("private-global-error");
    expect(reset).not.toHaveBeenCalled();
  });
});
