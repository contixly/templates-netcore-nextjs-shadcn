import { render, screen, within } from "@testing-library/react";

import { DashboardSkeleton } from "@/src/features/dashboard/ui/dashboard-skeleton";
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

test("dashboard cards retain separate reference trend and detail lines", () => {
  render(<SectionCards />);

  const revenueCard = screen
    .getByText("Total revenue")
    .closest<HTMLElement>("[data-slot='card']");
  expect(revenueCard).not.toBeNull();
  expect(within(revenueCard!).getByText("Trending up this month")).toHaveClass(
    "font-medium",
  );
  expect(
    within(revenueCard!).getByText("Visitors for the last 6 months"),
  ).toHaveClass("text-muted-foreground");
});

test("dashboard card skeletons preserve the primitive card slot", () => {
  render(<DashboardSkeleton label="Loading dashboard" />);

  expect(screen.getAllByTestId("dashboard-card-skeleton")).toHaveLength(4);
  for (const card of screen.getAllByTestId("dashboard-card-skeleton")) {
    expect(card).toHaveAttribute("data-slot", "card");
    expect(
      card.querySelectorAll('[data-slot="card-footer"] [data-slot="skeleton"]'),
    ).toHaveLength(2);
  }
  expect(screen.getByTestId("dashboard-chart-skeleton")).toHaveAttribute(
    "data-slot",
    "card",
  );
});
