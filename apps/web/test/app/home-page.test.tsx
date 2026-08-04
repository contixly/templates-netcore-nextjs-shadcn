import { render, screen } from "@testing-library/react";

import HomePage from "@/src/app/(public)/(home)/page";

const mockServerSystemStatus = jest.fn(() => <div>SSR status</div>);
const mockBrowserSystemStatus = jest.fn(() => <div>Browser status</div>);

jest.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => {
    const values: Record<string, string> = {
      "application.landing.brand": "Application Template",
      "application.landing.brandHomeLabel": "Application Template home",
      "application.landing.navigationLabel": "Public navigation",
      "application.landing.loginAction": "Sign in",
      "application.landing.eyebrow": "ASP.NET Core 10 + Next.js",
      "application.landing.title": "Build the product, not the plumbing",
      "application.landing.description":
        "Start with a secure application foundation that keeps the API and web UI cleanly separated.",
      "application.landing.primaryAction": "Get started",
      "application.landing.secondaryAction": "Read the documentation",
      "application.landing.featuresTitle":
        "A clear boundary from browser to business logic",
      "application.landing.featuresDescription":
        "ASP.NET Core owns the API while Next.js stays focused on the product experience.",
      "application.landing.features.api.title": "ASP.NET Core API",
      "application.landing.features.api.description":
        "ASP.NET Core 10 owns REST endpoints, authorization, and business rules.",
      "application.landing.features.web.title": "REST-only web UI",
      "application.landing.features.web.description":
        "Next.js calls the generated REST client without direct database access.",
      "application.landing.features.sessions.title": "Secure browser sessions",
      "application.landing.features.sessions.description":
        "Same-origin HttpOnly cookies keep browser authentication out of client storage.",
      "application.landing.features.workspaces.title":
        "Product-ready collaboration",
      "application.landing.features.workspaces.description":
        "Workspace, account, invitation, and API-key flows provide a stable starting point.",
      "application.landing.valueEyebrow": "Reusable by design",
      "application.landing.valueTitle":
        "Keep the foundation. Make the product yours.",
      "application.landing.valueDescription":
        "Use the shared shell and documented boundaries, then add only the domain behavior your product needs.",
      "application.landing.footer": "© 2026 Application Template",
      "application.landing.footerDescription":
        "A target-architecture starter for ASP.NET Core and Next.js.",
      "application.shell.navigation.documentation": "Documentation",
    };

    return (key: string) => values[`${namespace}.${key}`] ?? key;
  },
}));

jest.mock("@/src/components/application/theme-switcher", () => ({
  ThemeSwitcher: () => <button aria-label="Toggle theme" type="button" />,
}));

jest.mock("@/src/components/system/server-system-status", () => ({
  ServerSystemStatus: () => mockServerSystemStatus(),
}));

jest.mock("@/src/components/system/browser-system-status", () => ({
  BrowserSystemStatus: () => mockBrowserSystemStatus(),
}));

describe("HomePage", () => {
  it("renders the public target-architecture landing without status diagnostics", async () => {
    render(await HomePage());

    expect(
      screen.getByRole("heading", {
        level: 1,
        name: "Build the product, not the plumbing",
      }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Get started" })).toHaveAttribute(
      "href",
      "/auth/login?redirect=%2Fdashboard",
    );
    expect(
      screen.getByRole("link", { name: "Read the documentation" }),
    ).toHaveAttribute("href", "/docs");
    expect(
      screen.getByText(/ASP\.NET Core 10 owns REST endpoints/),
    ).toBeVisible();
    expect(
      screen.queryByText(/Better Auth|Prisma|Server Actions/),
    ).not.toBeInTheDocument();
    expect(mockServerSystemStatus).not.toHaveBeenCalled();
    expect(mockBrowserSystemStatus).not.toHaveBeenCalled();
  });

  it("provides a keyboard-accessible public shell with exactly one main landmark", async () => {
    render(await HomePage());

    expect(screen.getAllByRole("main")).toHaveLength(1);
    expect(screen.getByRole("banner")).toBeInTheDocument();
    expect(
      screen.getByRole("navigation", { name: "Public navigation" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Application Template home" }),
    ).toHaveAttribute("href", "/");
    expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute(
      "href",
      "/auth/login?redirect=%2Fdashboard",
    );
    expect(screen.getByRole("button", { name: "Toggle theme" })).toBeEnabled();
    expect(screen.getByRole("contentinfo")).toBeInTheDocument();
  });
});
