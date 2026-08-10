import {
  act,
  fireEvent,
  screen,
  waitFor,
  within,
} from "@testing-library/react";

import { DocumentsHeader } from "@/src/features/documents/ui/documents-header";
import { searchDocuments } from "@/src/lib/api/documents/browser/search-documents";
import type { DocumentSearchResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiResult } from "@/src/lib/api/result";
import { renderWithMessages } from "@/test/support/render";

const push = jest.fn();

jest.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
}));

jest.mock("@/src/lib/api/documents/browser/search-documents", () => ({
  searchDocuments: jest.fn(),
}));

const mockedSearchDocuments = jest.mocked(searchDocuments);
const emptyResponse: DocumentSearchResponse = { pages: [], headings: [] };
const resultsResponse: DocumentSearchResponse = {
  pages: [
    {
      type: "page",
      title: "API keys",
      description: "Create and manage API keys.",
      href: "/docs/api/api-keys",
      group: "Developers",
      parentItem: "API",
    },
  ],
  headings: [
    {
      type: "heading",
      title: "Create a key",
      href: "/docs/api/api-keys#create-a-key",
      pageTitle: "API keys",
      group: "Developers",
      parentItem: "API",
    },
  ],
};

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });

  return { promise, reject, resolve };
}

function renderHeader() {
  return renderWithMessages(<DocumentsHeader onOpenNavigation={jest.fn()} />);
}

function openSearch(modifier: "ctrl" | "meta" = "ctrl") {
  fireEvent.keyDown(document, {
    key: "k",
    ctrlKey: modifier === "ctrl",
    metaKey: modifier === "meta",
  });
}

async function runDebounce() {
  await act(async () => {
    jest.advanceTimersByTime(250);
  });
}

beforeEach(() => {
  jest.useFakeTimers();
  jest.clearAllMocks();
  mockedSearchDocuments.mockResolvedValue({ ok: true, data: emptyResponse });
});

afterEach(() => {
  jest.runOnlyPendingTimers();
  jest.useRealTimers();
});

it.each(["ctrl", "meta"] as const)(
  "opens with the %s+K shortcut and waits for the full debounce",
  async (modifier) => {
    renderHeader();

    openSearch(modifier);

    expect(screen.getByRole("dialog", { name: "Search docs" })).toBeVisible();
    const input = screen.getByRole("searchbox", { name: "Search docs" });
    expect(input).toHaveAttribute("maxLength", "120");
    expect(screen.getByRole("status")).toHaveTextContent(
      "Loading search results",
    );

    fireEvent.change(input, { target: { value: "api" } });
    act(() => jest.advanceTimersByTime(249));
    expect(mockedSearchDocuments).not.toHaveBeenCalled();

    await act(async () => {
      jest.advanceTimersByTime(1);
    });

    expect(mockedSearchDocuments).toHaveBeenCalledWith({
      query: "api",
      locale: "en",
      signal: expect.any(AbortSignal),
    });
  },
);

it("groups page and heading results and navigates to their canonical hrefs", async () => {
  mockedSearchDocuments.mockResolvedValue({
    ok: true,
    data: resultsResponse,
  });
  renderHeader();
  fireEvent.click(screen.getByRole("button", { name: "Search docs" }));
  await runDebounce();

  const dialog = screen.getByRole("dialog", { name: "Search docs" });
  const listbox = within(dialog).getByRole("listbox", {
    name: "Search results",
  });
  expect(
    within(listbox).getByRole("group", { name: "Pages" }),
  ).toHaveTextContent("API keys");
  expect(
    within(listbox).getByRole("group", { name: "Page sections" }),
  ).toHaveTextContent("Create a key");

  fireEvent.click(
    within(listbox).getByRole("option", { name: /Create and manage API keys/ }),
  );

  expect(push).toHaveBeenCalledWith("/docs/api/api-keys");
  expect(
    screen.queryByRole("dialog", { name: "Search docs" }),
  ).not.toBeInTheDocument();

  fireEvent.click(screen.getByRole("button", { name: "Search docs" }));
  expect(screen.getByRole("searchbox", { name: "Search docs" })).toHaveValue(
    "",
  );
  await runDebounce();
  fireEvent.click(
    screen.getByRole("option", { name: /Create a key.*API keys/ }),
  );

  expect(push).toHaveBeenLastCalledWith("/docs/api/api-keys#create-a-key");
});

it("loads the empty query and distinguishes it from an empty filtered result", async () => {
  renderHeader();
  openSearch();

  expect(mockedSearchDocuments).not.toHaveBeenCalled();
  await runDebounce();
  expect(mockedSearchDocuments).toHaveBeenCalledWith(
    expect.objectContaining({ query: "", locale: "en" }),
  );
  expect(await screen.findByRole("status")).toHaveTextContent(
    "No documents found",
  );

  fireEvent.change(screen.getByRole("searchbox", { name: "Search docs" }), {
    target: { value: "missing" },
  });
  await runDebounce();

  expect(await screen.findByRole("status")).toHaveTextContent(
    "No results found",
  );
});

it("shows only localized unavailable copy for an API failure", async () => {
  mockedSearchDocuments.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "private_problem_code",
      status: 500,
      traceId: "private-trace-id",
    },
  });
  renderHeader();
  openSearch();
  await runDebounce();

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Search is temporarily unavailable",
  );
  expect(
    screen.queryByText(/private_problem_code|private-trace-id/),
  ).not.toBeInTheDocument();
});

it("aborts a replaced request and ignores its stale older success", async () => {
  const older = deferred<ApiResult<DocumentSearchResponse>>();
  const newer = deferred<ApiResult<DocumentSearchResponse>>();
  mockedSearchDocuments
    .mockReturnValueOnce(older.promise)
    .mockReturnValueOnce(newer.promise);
  renderHeader();
  openSearch();
  const input = screen.getByRole("searchbox", { name: "Search docs" });
  fireEvent.change(input, { target: { value: "older" } });
  await runDebounce();

  const olderSignal = mockedSearchDocuments.mock.calls[0]?.[0].signal;
  expect(olderSignal).toBeDefined();
  expect(olderSignal?.aborted).toBe(false);

  fireEvent.change(input, { target: { value: "newer" } });
  expect(olderSignal?.aborted).toBe(true);
  expect(screen.getByRole("status")).toHaveTextContent(
    "Loading search results",
  );
  await runDebounce();

  await act(async () => {
    newer.resolve({
      ok: true,
      data: {
        pages: [{ ...resultsResponse.pages[0]!, title: "Newer result" }],
        headings: [],
      },
    });
  });
  expect(screen.getByRole("option", { name: /Newer result/ })).toBeEnabled();

  await act(async () => {
    older.resolve({
      ok: true,
      data: {
        pages: [{ ...resultsResponse.pages[0]!, title: "Stale result" }],
        headings: [],
      },
    });
  });

  expect(
    screen.getByRole("option", { name: /Newer result/ }),
  ).toBeInTheDocument();
  expect(screen.queryByText("Stale result")).not.toBeInTheDocument();
});

it("blocks old results during replacement and Escape closes and resets", async () => {
  const replacement = deferred<ApiResult<DocumentSearchResponse>>();
  mockedSearchDocuments
    .mockResolvedValueOnce({ ok: true, data: resultsResponse })
    .mockReturnValueOnce(replacement.promise);
  renderHeader();
  openSearch();
  await runDebounce();

  const input = screen.getByRole("searchbox", { name: "Search docs" });
  fireEvent.change(input, { target: { value: "replacement" } });
  expect(screen.getAllByRole("option")).not.toHaveLength(0);
  for (const option of screen.getAllByRole("option")) {
    expect(option).toBeDisabled();
  }
  await runDebounce();
  const replacementSignal = mockedSearchDocuments.mock.calls[1]?.[0].signal;

  fireEvent.keyDown(document, { key: "Escape" });

  await waitFor(() => {
    expect(
      screen.queryByRole("dialog", { name: "Search docs" }),
    ).not.toBeInTheDocument();
  });
  expect(replacementSignal?.aborted).toBe(true);

  openSearch();
  expect(screen.getByRole("searchbox", { name: "Search docs" })).toHaveValue(
    "",
  );
  expect(screen.queryByText("API keys")).not.toBeInTheDocument();

  await act(async () => {
    replacement.reject(new DOMException("aborted", "AbortError"));
  });
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
});
