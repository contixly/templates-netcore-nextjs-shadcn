import { screen } from "@testing-library/react";

import { ApiKeyManagement } from "@/src/features/api-keys/ui/api-key-management";
import { apiKeyPage } from "@/test/components/api-keys/fixtures";
import { renderWithMessages } from "@/test/support/render";

test("API-key empty state uses the reference card/empty composition", () => {
  renderWithMessages(
    <ApiKeyManagement
      initialPage={{ items: [], nextCursor: null }}
      owner={{ kind: "personal" }}
    />,
  );

  expect(screen.getByRole("heading", { name: "API keys" })).toHaveClass(
    "text-sm",
  );
  expect(screen.getByText("No API keys yet")).toBeVisible();
  expect(document.querySelector("[data-slot='empty']")).not.toBeNull();
});

test("API-key rows use the reference permission preview and compact action menu", () => {
  renderWithMessages(
    <ApiKeyManagement initialPage={apiKeyPage} owner={{ kind: "personal" }} />,
  );

  expect(
    screen.getByText("Basic account read").closest("[data-slot='badge']"),
  ).not.toBeNull();
  const menu = screen.getByRole("button", {
    name: "Actions for CLI integration",
  });
  expect(menu).toHaveAttribute("data-slot", "dropdown-menu-trigger");
  expect(
    screen.queryByRole("button", { name: "Edit" }),
  ).not.toBeInTheDocument();
});
