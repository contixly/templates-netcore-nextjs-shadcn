import { render, screen } from "@testing-library/react";

import HomePage from "@/src/app/page";

describe("HomePage", () => {
  it("identifies the clean UI foundation", () => {
    render(<HomePage />);

    expect(
      screen.getByRole("heading", { name: "Next.js UI foundation" }),
    ).toBeInTheDocument();
  });
});
