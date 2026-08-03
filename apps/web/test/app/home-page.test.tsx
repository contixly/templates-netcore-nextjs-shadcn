import { render, screen } from "@testing-library/react";

import HomePage from "@/src/app/(public)/(home)/page";

jest.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => {
    const values: Record<string, string> = {
      "system.page.eyebrow": "Migration iteration 2",
      "system.page.title": "REST connectivity",
      "system.page.description":
        "The same generated SDK calls ASP.NET Core from server rendering and from the browser.",
      "system.page.getStarted": "Get Started",
      "system.status.loading": "Checking API status",
      "system.status.ssrTitle": "Server-rendered API status",
    };

    return (key: string) => values[`${namespace}.${key}`] ?? key;
  },
}));

jest.mock("@/src/components/system/server-system-status", () => ({
  ServerSystemStatus: () => <div data-testid="status-ssr">SSR status</div>,
}));

jest.mock("@/src/components/system/browser-system-status", () => ({
  BrowserSystemStatus: () => (
    <div data-testid="status-browser">Browser status</div>
  ),
}));

describe("HomePage", () => {
  it("renders only the technical iteration-2 vertical slice", async () => {
    render(await HomePage());

    expect(
      screen.getByRole("heading", { name: "REST connectivity" }),
    ).toBeInTheDocument();
    expect(screen.getByTestId("status-ssr")).toBeInTheDocument();
    expect(screen.getByTestId("status-browser")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Get Started" })).toHaveAttribute(
      "href",
      "/auth/login?redirect=%2Fdashboard",
    );
    expect(screen.queryByText(/sign in/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/workspace/i)).not.toBeInTheDocument();
  });
});
