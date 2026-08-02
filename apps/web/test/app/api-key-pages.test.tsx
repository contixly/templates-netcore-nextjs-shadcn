import { screen } from "@testing-library/react";

import ApiKeyPage from "@/src/app/(site)/user/api-keys/page";
import ApiKeyLoading from "@/src/app/(site)/user/api-keys/loading";
import OrganizationSwitcherSlot from "@/src/app/(site)/@organizationSwitcher/user/api-keys/page";
import { loadApiKeys } from "@/src/lib/api/api-keys/server/load-api-keys";
import { apiKeyPage } from "@/test/components/api-keys/fixtures";
import { renderWithMessages } from "@/test/support/render";

jest.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => {
    const values: Record<string, string> = {
      "apiKeys.page.title": "API keys",
      "apiKeys.page.description": "Personal automation credentials",
      "apiKeys.page.loading": "Loading API keys",
      "apiKeys.page.failureTitle": "API keys are unavailable",
      "apiKeys.page.failureDescription": "Try again",
    };
    return (key: string) => values[`${namespace}.${key}`] ?? key;
  },
}));
jest.mock("@/src/lib/api/api-keys/server/load-api-keys", () => ({
  loadApiKeys: jest.fn(),
}));
jest.mock("@/src/components/api-keys/api-key-management", () => ({
  ApiKeyManagement: ({
    initialPage,
  }: {
    initialPage: { items: unknown[] };
  }) => (
    <section data-testid="api-key-management">
      {initialPage.items.length}
    </section>
  ),
}));

const loadKeys = jest.mocked(loadApiKeys);

it("loads exactly the first personal page on the server", async () => {
  loadKeys.mockResolvedValue({ ok: true, data: apiKeyPage });
  renderWithMessages(await ApiKeyPage());

  expect(loadKeys).toHaveBeenCalledTimes(1);
  expect(loadKeys).toHaveBeenCalledWith({ kind: "personal" }, { limit: 50 });
  expect(screen.getByRole("heading", { name: "API keys" })).toBeVisible();
  expect(screen.getByTestId("api-key-management")).toHaveTextContent("1");
});

it("renders a localized safe failure without exposing backend detail", async () => {
  loadKeys.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "internal_error",
      status: 500,
      traceId: "trace-safe",
    },
  });
  renderWithMessages(await ApiKeyPage());

  expect(screen.getByRole("alert")).toHaveTextContent(
    "API keys are unavailable",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-safe");
});

it("provides localized loading and a complete organization-switcher parallel slot", async () => {
  renderWithMessages(await ApiKeyLoading());
  expect(screen.getByRole("status")).toHaveTextContent("Loading API keys");
  expect(OrganizationSwitcherSlot()).toBeNull();
});
