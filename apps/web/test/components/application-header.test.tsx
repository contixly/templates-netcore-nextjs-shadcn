import { fireEvent, render, screen } from "@testing-library/react";

import { ApplicationHeader } from "@/src/features/application/ui/application-header";
import { SidebarProvider } from "@/src/components/ui/sidebar";

let mockIsMobile = false;
let mockPathname = "/w/acme/dashboard";

jest.mock("next/navigation", () => ({
  usePathname: () => mockPathname,
}));

jest.mock("next-intl", () => ({
  useTranslations: () => (key: string) =>
    ({
      "breadcrumbs.home": "Home",
      "breadcrumbs.dashboard": "Dashboard",
      "breadcrumbs.settings": "Settings",
      "invitationDecision.title": "Workspace invitation",
      "navigation.documentation": "Documentation",
      "sidebar.close": "Close sidebar",
      "sidebar.open": "Open sidebar",
    })[key] ?? key,
}));

jest.mock("@/src/features/application/ui/theme-switcher", () => ({
  ThemeSwitcher: () => <button type="button">Toggle theme</button>,
}));

jest.mock("@/src/hooks/use-mobile", () => ({
  useIsMobile: () => mockIsMobile,
}));

beforeEach(() => {
  mockIsMobile = false;
  mockPathname = "/w/acme/dashboard";
});

it("renders accessible shell controls and route breadcrumbs", () => {
  render(
    <SidebarProvider defaultOpen={false}>
      <ApplicationHeader />
    </SidebarProvider>,
  );

  expect(screen.getByRole("banner")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Open sidebar" })).toBeEnabled();
  expect(screen.getByRole("link", { name: "Documentation" })).toHaveAttribute(
    "href",
    "/docs",
  );
  expect(screen.getByRole("button", { name: "Toggle theme" })).toBeEnabled();
  expect(
    screen.getByRole("navigation", { name: "Breadcrumb" }),
  ).toBeInTheDocument();
  expect(screen.getByText("Dashboard")).toBeInTheDocument();
});

it("centers the bounded separator within the header row", () => {
  const { container } = render(
    <SidebarProvider defaultOpen={false}>
      <ApplicationHeader />
    </SidebarProvider>,
  );

  const separator = container.querySelector(
    "[data-slot='application-header'] [data-slot='separator']",
  );
  expect(separator).toHaveClass("data-vertical:self-center");
  expect(separator).not.toHaveClass("data-vertical:self-stretch");
});

it("labels the trigger from the desktop sidebar state", () => {
  render(
    <SidebarProvider defaultOpen={false}>
      <ApplicationHeader />
    </SidebarProvider>,
  );

  fireEvent.click(screen.getByRole("button", { name: "Open sidebar" }));
  expect(screen.getByRole("button", { name: "Close sidebar" })).toBeEnabled();
});

it("labels the trigger from the mobile drawer state", () => {
  mockIsMobile = true;
  render(
    <SidebarProvider defaultOpen>
      <ApplicationHeader />
    </SidebarProvider>,
  );

  fireEvent.click(screen.getByRole("button", { name: "Open sidebar" }));
  expect(screen.getByRole("button", { name: "Close sidebar" })).toBeEnabled();
});

it("uses the localized invitation page title for invitation breadcrumbs", () => {
  mockPathname = "/invite/invitation-id";
  render(
    <SidebarProvider>
      <ApplicationHeader />
    </SidebarProvider>,
  );

  expect(screen.getByText("Workspace invitation")).toHaveAttribute(
    "aria-current",
    "page",
  );
  expect(screen.queryByText("Settings")).not.toBeInTheDocument();
});
