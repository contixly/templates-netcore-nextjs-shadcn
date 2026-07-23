import { render, screen } from "@testing-library/react";

import HomePage from "@/src/app/page";

jest.mock("next-intl/server", () => ({
  getTranslations: async () => {
    const messages: Record<string, string> = {
      eyebrow: "Migration iteration 2",
      title: "REST connectivity",
      description:
        "The same generated SDK calls ASP.NET Core from server rendering and from the browser.",
    };

    return (key: string) => messages[key] ?? key;
  },
}));

describe("HomePage", () => {
  it("renders only the technical iteration-2 copy", async () => {
    render(await HomePage());

    expect(
      screen.getByRole("heading", { name: "REST connectivity" }),
    ).toBeInTheDocument();
    expect(screen.queryByText(/sign in/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/workspace/i)).not.toBeInTheDocument();
  });
});
