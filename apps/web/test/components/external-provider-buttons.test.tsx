import { fireEvent, screen, waitFor } from "@testing-library/react";

import { ExternalProviderButtons } from "@/src/components/authentication/external-provider-buttons";
import { authenticationRoutes } from "@/src/features/authentication/authentication-routes";
import { startExternalAuth } from "@/src/lib/api/auth/browser/start-external-auth";
import type { AuthProviderResponse } from "@/src/lib/api/generated";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/auth/browser/start-external-auth", () => ({
  startExternalAuth: jest.fn(),
}));

const google = {
  id: "google",
  displayName: "Google",
} satisfies AuthProviderResponse;
const github = {
  id: "github",
  displayName: "GitHub",
} satisfies AuthProviderResponse;
const startExternalAuthMock = jest.mocked(startExternalAuth);

type LocationImplementation = {
  assign(url: string): void;
};

function locationImplementation(): LocationImplementation {
  const implementationSymbol = Object.getOwnPropertySymbols(
    window.location,
  ).find((symbol) => symbol.description === "impl");

  if (!implementationSymbol) {
    throw new Error("JSDOM location implementation is unavailable.");
  }

  return (window.location as unknown as Record<symbol, LocationImplementation>)[
    implementationSymbol
  ];
}

let assignSpy: jest.SpiedFunction<LocationImplementation["assign"]>;

beforeEach(() => {
  jest.clearAllMocks();
  assignSpy = jest
    .spyOn(locationImplementation(), "assign")
    .mockImplementation(() => undefined);
});

afterEach(() => {
  assignSpy.mockRestore();
});

it("renders buttons only for providers advertised by capabilities", () => {
  renderWithMessages(
    <ExternalProviderButtons
      providers={[google]}
      returnUrl={authenticationRoutes.dashboard}
    />,
  );

  expect(
    screen.getByRole("button", { name: "Continue with Google" }),
  ).toBeEnabled();
  expect(
    screen.queryByRole("button", { name: "Continue with GitHub" }),
  ).not.toBeInTheDocument();
});

it("navigates only to the API-issued authorization URL", async () => {
  const authorizationUrl =
    "https://accounts.google.com/o/oauth2/v2/auth?state=safe";
  startExternalAuthMock.mockResolvedValue({
    ok: true,
    data: { authorizationUrl },
  });
  renderWithMessages(
    <ExternalProviderButtons
      providers={[google]}
      returnUrl={authenticationRoutes.dashboard}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Continue with Google" }));

  await waitFor(() => {
    expect(assignSpy).toHaveBeenCalledWith(authorizationUrl);
  });
  expect(startExternalAuthMock).toHaveBeenCalledWith({
    provider: "google",
    intent: "signIn",
    returnUrl: "/dashboard",
  });
});

it("allows only one provider challenge to be in flight", async () => {
  let resolveChallenge:
    | ((result: Awaited<ReturnType<typeof startExternalAuth>>) => void)
    | undefined;
  startExternalAuthMock.mockReturnValue(
    new Promise((resolve) => {
      resolveChallenge = resolve;
    }),
  );
  renderWithMessages(
    <ExternalProviderButtons
      providers={[google, github]}
      returnUrl={authenticationRoutes.dashboard}
    />,
  );

  const googleButton = screen.getByRole("button", {
    name: "Continue with Google",
  });
  const githubButton = screen.getByRole("button", {
    name: "Continue with GitHub",
  });
  fireEvent.click(googleButton);
  fireEvent.click(googleButton);
  fireEvent.click(githubButton);

  expect(startExternalAuthMock).toHaveBeenCalledTimes(1);
  expect(googleButton).toBeDisabled();
  expect(githubButton).toBeDisabled();

  resolveChallenge?.({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "The authentication API is unavailable.",
  );
  await waitFor(() => {
    expect(googleButton).toBeEnabled();
    expect(githubButton).toBeEnabled();
  });
  expect(assignSpy).not.toHaveBeenCalled();
});

it.each([
  "http://accounts.google.com/o/oauth2/v2/auth",
  "/relative-provider-authorization",
  "javascript:alert('unsafe')",
])("fails safely for a non-HTTPS authorization URL %p", async (url) => {
  startExternalAuthMock.mockResolvedValue({
    ok: true,
    data: { authorizationUrl: url },
  });
  renderWithMessages(
    <ExternalProviderButtons
      providers={[google]}
      returnUrl={authenticationRoutes.dashboard}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Continue with Google" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "The sign-in destination was rejected.",
  );
  expect(assignSpy).not.toHaveBeenCalled();
  expect(
    screen.getByRole("button", { name: "Continue with Google" }),
  ).toBeEnabled();
});
