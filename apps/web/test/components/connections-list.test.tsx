import { fireEvent, screen, waitFor, within } from "@testing-library/react";

import { ConnectionsList } from "@/src/components/account/connections-list";
import { disconnectBrowserAccountProvider } from "@/src/lib/api/account/browser/account-mutations";
import { startExternalAuth } from "@/src/lib/api/auth/browser/start-external-auth";
import {
  getAccountConnections,
  type AccountConnectionsResponse,
} from "@/src/lib/api/generated";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/account/browser/account-mutations", () => ({
  disconnectBrowserAccountProvider: jest.fn(),
}));
jest.mock("@/src/lib/api/auth/browser/start-external-auth", () => ({
  startExternalAuth: jest.fn(),
}));
jest.mock("@/src/lib/api/generated", () => ({
  getAccountConnections: jest.fn(),
}));

const initialConnections = {
  items: [
    {
      provider: "google",
      displayName: "Google",
      configured: true,
      connected: true,
      email: "google@example.test",
      connectedAt: "2026-07-20T10:00:00Z",
      lastUsedAt: "2026-07-28T10:00:00Z",
      isCurrentAuthenticationMethod: true,
      canConnect: false,
      canDisconnect: false,
      disabledReason: "external_connection_required",
    },
    {
      provider: "github",
      displayName: "GitHub",
      configured: true,
      connected: false,
      email: null,
      connectedAt: null,
      lastUsedAt: null,
      isCurrentAuthenticationMethod: false,
      canConnect: true,
      canDisconnect: false,
      disabledReason: null,
    },
    {
      provider: "gitlab",
      displayName: "GitLab",
      configured: false,
      connected: true,
      email: "gitlab@example.test",
      connectedAt: "2026-07-18T10:00:00Z",
      lastUsedAt: null,
      isCurrentAuthenticationMethod: false,
      canConnect: false,
      canDisconnect: true,
      disabledReason: null,
    },
  ],
} satisfies AccountConnectionsResponse;

const startAuth = jest.mocked(startExternalAuth);
const disconnect = jest.mocked(disconnectBrowserAccountProvider);
const getConnections = jest.mocked(getAccountConnections);

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise;
  });
  return { promise, resolve };
}

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

it("subordinates provider headings beneath a settings section heading", () => {
  renderWithMessages(
    <ConnectionsList
      headingLevel={3}
      initialConnections={initialConnections}
    />,
  );

  for (const name of ["Google", "GitHub", "GitLab"]) {
    expect(screen.getByRole("heading", { level: 3, name })).toBeVisible();
  }
  expect(screen.queryByRole("heading", { level: 2 })).not.toBeInTheDocument();
});

it("renders configured, connected, current, and disabled server states", () => {
  renderWithMessages(
    <ConnectionsList initialConnections={initialConnections} />,
  );

  const google = screen.getByRole("article", { name: "Google connection" });
  expect(google).toHaveTextContent("Connected");
  expect(google).toHaveTextContent("google@example.test");
  expect(google).toHaveTextContent("Current sign-in method");
  expect(google).toHaveTextContent(
    "The server requires this connection to remain available.",
  );
  expect(
    within(google).getByRole("button", { name: "Disconnect Google" }),
  ).toBeDisabled();

  const github = screen.getByRole("article", { name: "GitHub connection" });
  expect(github).toHaveTextContent("Not connected");
  expect(
    within(github).getByRole("button", { name: "Connect GitHub" }),
  ).toBeEnabled();

  const gitlab = screen.getByRole("article", { name: "GitLab connection" });
  expect(gitlab).toHaveTextContent("Provider configuration is unavailable");
  expect(
    within(gitlab).getByRole("button", { name: "Disconnect GitLab" }),
  ).toBeEnabled();
});

it("keeps a configured candidate disabled when only unconfigured logins would survive", () => {
  const projection = {
    items: [
      {
        ...initialConnections.items[0],
        configured: false,
      },
      {
        ...initialConnections.items[1],
        connected: true,
        email: "github@example.test",
        connectedAt: "2026-07-20T10:00:00Z",
        lastUsedAt: "2026-07-28T10:00:00Z",
        canConnect: false,
        canDisconnect: false,
        disabledReason: "external_connection_required",
      },
      {
        ...initialConnections.items[2],
        canDisconnect: false,
        disabledReason: "external_connection_required",
      },
    ],
  } satisfies AccountConnectionsResponse;
  renderWithMessages(<ConnectionsList initialConnections={projection} />);

  const github = screen.getByRole("article", { name: "GitHub connection" });
  const disconnectGithub = within(github).getByRole("button", {
    name: "Disconnect GitHub",
  });

  expect(github).toHaveTextContent(
    "The server requires this connection to remain available.",
  );
  expect(disconnectGithub).toBeDisabled();
  fireEvent.click(disconnectGithub);
  expect(disconnect).not.toHaveBeenCalled();
});

it("connects only through the API-issued OAuth navigation", async () => {
  const authorizationUrl =
    "https://github.com/login/oauth/authorize?state=opaque";
  startAuth.mockResolvedValue({
    ok: true,
    data: { authorizationUrl },
  });
  renderWithMessages(
    <ConnectionsList initialConnections={initialConnections} />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Connect GitHub" }));

  await waitFor(() => {
    expect(startAuth).toHaveBeenCalledWith({
      provider: "github",
      intent: "connect",
      returnUrl: "/user/connections",
    });
    expect(assignSpy).toHaveBeenCalledWith(authorizationUrl);
  });
});

it("rejects an unsafe authorization destination and recovers", async () => {
  startAuth.mockResolvedValue({
    ok: true,
    data: { authorizationUrl: "javascript:alert('unsafe')" },
  });
  renderWithMessages(
    <ConnectionsList initialConnections={initialConnections} />,
  );

  const connect = screen.getByRole("button", { name: "Connect GitHub" });
  fireEvent.click(connect);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "The connection destination was rejected.",
  );
  expect(assignSpy).not.toHaveBeenCalled();
  expect(connect).toBeEnabled();
});

it("applies a confirmed disconnect and preserves configured providers", async () => {
  disconnect.mockResolvedValue({
    ok: true,
    data: { provider: "gitlab" },
  });
  const configuredConnection = {
    ...initialConnections.items[2],
    configured: true,
  };
  const disconnectedConnection = {
    ...configuredConnection,
    connected: false,
    email: null,
    connectedAt: null,
    lastUsedAt: null,
    isCurrentAuthenticationMethod: false,
    canConnect: true,
    canDisconnect: false,
    disabledReason: null,
  };
  getConnections.mockResolvedValue({
    data: {
      data: { items: [disconnectedConnection] },
    },
  } as Awaited<ReturnType<typeof getAccountConnections>>);
  renderWithMessages(
    <ConnectionsList initialConnections={{ items: [configuredConnection] }} />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Disconnect GitLab" }));

  expect(await screen.findByRole("status")).toHaveTextContent(
    "GitLab disconnected.",
  );
  expect(disconnect).toHaveBeenCalledWith({ id: "browser-client" }, "gitlab");
  expect(
    screen.getByRole("article", { name: "GitLab connection" }),
  ).toHaveTextContent("Not connected");
});

it("reloads disconnect policy after removing one of two configured local-session connections", async () => {
  const google = {
    ...initialConnections.items[0],
    isCurrentAuthenticationMethod: false,
    canDisconnect: true,
    disabledReason: null,
  };
  const github = {
    ...initialConnections.items[1],
    connected: true,
    email: "github@example.test",
    connectedAt: "2026-07-21T10:00:00Z",
    lastUsedAt: "2026-07-29T10:00:00Z",
    canConnect: false,
    canDisconnect: true,
  };
  disconnect.mockResolvedValue({
    ok: true,
    data: { provider: "github" },
  });
  getConnections.mockResolvedValue({
    data: {
      data: {
        items: [
          {
            ...google,
            canDisconnect: false,
            disabledReason: "external_connection_required",
          },
          {
            ...github,
            connected: false,
            email: null,
            connectedAt: null,
            lastUsedAt: null,
            canConnect: true,
            canDisconnect: false,
            disabledReason: null,
          },
        ],
      },
    },
  } as Awaited<ReturnType<typeof getAccountConnections>>);
  renderWithMessages(
    <ConnectionsList initialConnections={{ items: [google, github] }} />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Disconnect GitHub" }));

  await waitFor(() => {
    expect(getConnections).toHaveBeenCalledWith({
      client: { id: "browser-client" },
      cache: "no-store",
    });
  });
  const survivor = screen.getByRole("article", { name: "Google connection" });
  expect(
    within(survivor).getByRole("button", { name: "Disconnect Google" }),
  ).toBeDisabled();
  expect(survivor).toHaveTextContent(
    "The server requires this connection to remain available.",
  );
});

it("preserves a successful disconnect with a conservative projection until refresh retry succeeds", async () => {
  const google = {
    ...initialConnections.items[0],
    isCurrentAuthenticationMethod: false,
    canDisconnect: true,
    disabledReason: null,
  };
  const github = {
    ...initialConnections.items[1],
    connected: true,
    email: "github@example.test",
    connectedAt: "2026-07-21T10:00:00Z",
    lastUsedAt: "2026-07-29T10:00:00Z",
    canConnect: false,
    canDisconnect: true,
  };
  const disconnectedGithub = {
    ...github,
    connected: false,
    email: null,
    connectedAt: null,
    lastUsedAt: null,
    isCurrentAuthenticationMethod: false,
    canConnect: true,
    canDisconnect: false,
    disabledReason: null,
  };
  disconnect.mockResolvedValue({
    ok: true,
    data: { provider: "github" },
  });
  const firstRefresh = deferred<unknown>();
  getConnections
    .mockReturnValueOnce(
      firstRefresh.promise as ReturnType<typeof getAccountConnections>,
    )
    .mockResolvedValueOnce({
      data: {
        data: {
          items: [
            {
              ...google,
              canDisconnect: false,
              disabledReason: "external_connection_required",
            },
            disconnectedGithub,
          ],
        },
      },
    } as Awaited<ReturnType<typeof getAccountConnections>>);
  renderWithMessages(
    <ConnectionsList initialConnections={{ items: [google, github] }} />,
  );

  fireEvent.click(screen.getByRole("button", { name: "Disconnect GitHub" }));

  await waitFor(() => {
    expect(getConnections).toHaveBeenCalledTimes(1);
  });
  const survivor = screen.getByRole("article", { name: "Google connection" });
  expect(
    within(survivor).getByRole("button", { name: "Disconnect Google" }),
  ).toBeDisabled();
  expect(
    screen.getByRole("article", { name: "GitHub connection" }),
  ).toHaveTextContent("Not connected");
  expect(
    screen.queryByRole("button", { name: "Disconnect GitHub" }),
  ).not.toBeInTheDocument();

  firstRefresh.resolve({
    error: {
      code: "api_unavailable",
      traceId: "trace-connections-refresh",
    },
    response: { status: 503 } as Response,
  });

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "GitHub was disconnected, but connections could not be refreshed.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent(
    "trace-connections-refresh",
  );
  expect(screen.getByRole("alert")).not.toHaveTextContent(
    "The provider could not be disconnected.",
  );
  expect(
    within(survivor).getByRole("button", { name: "Disconnect Google" }),
  ).toBeDisabled();
  expect(survivor).toHaveTextContent(
    "The server requires this connection to remain available.",
  );
  fireEvent.click(
    screen.getByRole("button", { name: "Retry connection list refresh" }),
  );

  expect(await screen.findByRole("status")).toHaveTextContent(
    "GitHub disconnected.",
  );
  expect(getConnections).toHaveBeenCalledTimes(2);
  expect(getConnections).toHaveBeenNthCalledWith(2, {
    client: { id: "browser-client" },
    cache: "no-store",
  });
  expect(disconnect).toHaveBeenCalledTimes(1);
  expect(
    screen.queryByRole("button", { name: "Retry connection list refresh" }),
  ).not.toBeInTheDocument();
});

it("renders disconnect server failures and re-enables the action", async () => {
  disconnect.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "external_connection_required",
      status: 409,
      traceId: "trace-disconnect",
    },
  });
  renderWithMessages(
    <ConnectionsList initialConnections={initialConnections} />,
  );

  const disconnectButton = screen.getByRole("button", {
    name: "Disconnect GitLab",
  });
  fireEvent.click(disconnectButton);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "The provider could not be disconnected.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-disconnect");
  expect(disconnectButton).toBeEnabled();
});
