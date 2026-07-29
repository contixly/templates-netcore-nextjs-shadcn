import { render, screen } from "@testing-library/react";

import AuthErrorPage from "@/src/app/(simple)/auth/error/page";

jest.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) =>
    ({
      eyebrow: "Authentication",
      "codes.externalEmailUnverified.title": "Email could not be verified",
      "codes.externalEmailUnverified.description":
        "The provider did not confirm a verified email for this account.",
      "codes.generic.title": "Sign-in could not be completed",
      "codes.generic.description":
        "Return to sign in and start a new authentication attempt.",
      retry: "Try sign-in again",
      home: "Return home",
    })[key] ?? key,
}));

it("maps an allow-listed callback code to localized safe copy", async () => {
  render(
    await AuthErrorPage({
      searchParams: Promise.resolve({ code: "external_email_unverified" }),
    }),
  );

  expect(
    screen.getByRole("heading", { name: "Email could not be verified" }),
  ).toBeInTheDocument();
  expect(
    screen.getByText(
      "The provider did not confirm a verified email for this account.",
    ),
  ).toBeInTheDocument();
  expect(
    screen.getByRole("link", { name: "Try sign-in again" }),
  ).toHaveAttribute("href", "/auth/login");
  expect(screen.getByRole("link", { name: "Return home" })).toHaveAttribute(
    "href",
    "/",
  );
});

it("uses the generic fallback without echoing arbitrary provider query text", async () => {
  render(
    await AuthErrorPage({
      searchParams: Promise.resolve({
        code: "raw_provider_failure",
        error: "access_denied",
        error_description: "private provider explanation",
        provider: "private-provider-name",
      }),
    }),
  );

  expect(
    screen.getByRole("heading", {
      name: "Sign-in could not be completed",
    }),
  ).toBeInTheDocument();
  expect(screen.queryByText("raw_provider_failure")).not.toBeInTheDocument();
  expect(screen.queryByText("access_denied")).not.toBeInTheDocument();
  expect(
    screen.queryByText("private provider explanation"),
  ).not.toBeInTheDocument();
  expect(screen.queryByText("private-provider-name")).not.toBeInTheDocument();
});

it("does not select a provider error from duplicate code parameters", async () => {
  render(
    await AuthErrorPage({
      searchParams: Promise.resolve({
        code: ["external_email_unverified", "external_auth_failed"],
      }),
    }),
  );

  expect(
    screen.getByRole("heading", {
      name: "Sign-in could not be completed",
    }),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole("heading", { name: "Email could not be verified" }),
  ).not.toBeInTheDocument();
});
