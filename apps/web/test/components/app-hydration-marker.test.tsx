import { render, waitFor } from "@testing-library/react";

import { AppHydrationMarker } from "@/src/features/application/ui/app-hydration-marker";

afterEach(() => {
  delete document.documentElement.dataset.appHydrated;
});

it("publishes an explicit readiness marker only after the client effect runs", async () => {
  expect(document.documentElement).not.toHaveAttribute("data-app-hydrated");

  render(<AppHydrationMarker />);

  await waitFor(() => {
    expect(document.documentElement).toHaveAttribute(
      "data-app-hydrated",
      "true",
    );
  });
});
