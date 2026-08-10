import { render, screen } from "@testing-library/react";

import { SectionCards } from "@/src/features/dashboard/ui/section-cards";

test("dashboard metrics retain the reference card grid region", () => {
  render(<SectionCards />);

  expect(
    screen.getByRole("region", { name: /dashboard metrics/i }),
  ).toHaveClass("*:data-[slot=card]:bg-gradient-to-t");
  expect(screen.getAllByRole("article")[0]).toHaveAttribute(
    "data-slot",
    "card",
  );
});
