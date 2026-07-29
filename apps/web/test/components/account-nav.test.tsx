import { NextIntlClientProvider } from "next-intl";
import { screen } from "@testing-library/react";

import {
  AuthenticatedAccountShell,
  default as UserLayout,
} from "@/src/app/(site)/user/layout";
import { AccountNav } from "@/src/components/account/account-nav";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import accountRu from "@/src/messages/account.ru.json";
import { renderWithMessages, withMessages } from "@/test/support/render";

jest.mock("next/server", () => ({
  connection: jest.fn().mockResolvedValue(undefined),
}));
jest.mock("next/navigation", () => ({
  redirect: jest.fn((path: string) => {
    throw new Error(`NEXT_REDIRECT:${path}`);
  }),
  usePathname: jest.fn(() => "/user/profile"),
}));
jest.mock("@/src/lib/api/auth/server/load-server-auth-session", () => ({
  loadServerAuthSession: jest.fn(),
}));
jest.mock("@/src/components/authentication/auth-api-failure", () => ({
  AuthApiFailure: () => (
    <section role="alert">Authentication is unavailable</section>
  ),
}));

const loadSession = jest.mocked(loadServerAuthSession);
const redirect = jest.mocked(jest.requireMock("next/navigation").redirect);
const usePathname = jest.mocked(
  jest.requireMock("next/navigation").usePathname,
);

beforeEach(() => {
  jest.clearAllMocks();
});

it("renders only iteration-four account destinations", () => {
  renderWithMessages(<AccountNav pathname="/user/profile" />);

  expect(screen.getAllByRole("link").map((link) => link.textContent)).toEqual([
    "Profile",
    "Connections",
    "Security",
    "Danger",
  ]);
  expect(
    screen.getAllByRole("link").map((link) => link.getAttribute("href")),
  ).toEqual([
    "/user/profile",
    "/user/connections",
    "/user/security",
    "/user/danger",
  ]);
  expect(
    screen.getByRole("navigation", { name: "Account settings" }),
  ).toBeInTheDocument();
  expect(screen.queryByText("Invitations")).not.toBeInTheDocument();
  expect(screen.queryByText("API Keys")).not.toBeInTheDocument();
  expect(usePathname).not.toHaveBeenCalled();
});

it("marks exact and nested destinations active without prefix collisions", () => {
  const { rerender } = renderWithMessages(
    <AccountNav pathname="/user/security/sessions/current" />,
  );

  expect(screen.getByRole("link", { name: "Security" })).toHaveAttribute(
    "aria-current",
    "page",
  );
  expect(screen.getByRole("link", { name: "Profile" })).not.toHaveAttribute(
    "aria-current",
  );

  rerender(withMessages(<AccountNav pathname="/user/profiled" />));

  for (const link of screen.getAllByRole("link")) {
    expect(link).not.toHaveAttribute("aria-current");
  }
});

it("keeps the account navigation responsive", () => {
  renderWithMessages(<AccountNav pathname="/user/connections" />);

  expect(screen.getByRole("list")).toHaveClass(
    "overflow-x-auto",
    "md:flex-col",
  );
});

it("uses the fixed Russian deployment locale for account navigation", () => {
  renderWithMessages(
    <NextIntlClientProvider
      locale="ru"
      messages={{ account: accountRu }}
      timeZone="UTC"
    >
      <AccountNav pathname="/user/profile" />
    </NextIntlClientProvider>,
  );

  expect(screen.getAllByRole("link").map((link) => link.textContent)).toEqual([
    "Профиль",
    "Подключения",
    "Безопасность",
    "Опасная зона",
  ]);
});

it("redirects an explicit anonymous account shell to the profile login return", async () => {
  loadSession.mockResolvedValue({
    ok: true,
    data: { authenticated: false, user: null, session: null },
  });

  await expect(
    AuthenticatedAccountShell({ children: <p>Protected account</p> }),
  ).rejects.toThrow("NEXT_REDIRECT:/auth/login?redirect=%2Fuser%2Fprofile");
});

it("renders a safe failure instead of redirecting on an auth API failure", async () => {
  loadSession.mockResolvedValue({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });

  renderWithMessages(
    await AuthenticatedAccountShell({
      children: <p>Protected account</p>,
    }),
  );

  expect(screen.getByRole("alert")).toHaveTextContent(
    "Authentication is unavailable",
  );
  expect(redirect).not.toHaveBeenCalled();
});

it("renders protected children only after server-confirmed authentication", async () => {
  loadSession.mockResolvedValue({
    ok: true,
    data: {
      authenticated: true,
      user: {
        id: "01900000-0000-7000-8000-000000000001",
        name: "Account User",
        email: "account@example.test",
        emailVerified: true,
        image: null,
      },
      session: {
        id: "01900000-0000-7000-8000-000000000002",
        createdAt: "2026-07-29T00:00:00Z",
        updatedAt: "2026-07-29T00:00:00Z",
        expiresAt: "2026-08-05T00:00:00Z",
      },
    },
  });

  renderWithMessages(
    await AuthenticatedAccountShell({
      children: <p>Protected account</p>,
    }),
  );

  expect(screen.getByText("Protected account")).toBeInTheDocument();
  expect(
    screen.getByRole("navigation", { name: "Account settings" }),
  ).toBeInTheDocument();
});

it("places the async auth gate below a suspense boundary", () => {
  const layout = UserLayout({ children: <p>Protected account</p> });

  expect(layout.type).toBe(Symbol.for("react.suspense"));
});
