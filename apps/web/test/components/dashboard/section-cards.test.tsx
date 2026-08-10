import { screen } from "@testing-library/react";

import { SectionCards } from "@/src/features/dashboard/ui/section-cards";
import { renderWithMessages } from "@/test/support/render";

it("renders the four localized dashboard metrics", () => {
  renderWithMessages(<SectionCards />);

  expect(screen.getByText("$1,250.00")).toBeInTheDocument();
  expect(screen.getByText("1,234")).toBeInTheDocument();
  expect(screen.getByText("45,678")).toBeInTheDocument();
  expect(screen.getByText("4.5%")).toBeInTheDocument();
  expect(screen.getAllByRole("article")).toHaveLength(4);
  expect(screen.getByText("Total revenue")).toBeVisible();
});

it("localizes the metric-section accessible name", () => {
  renderWithMessages(
    <SectionCards
      copy={{
        sectionLabel: "Показатели панели",
        revenue: { label: "Выручка", trend: "Рост", detail: "Посетители" },
        customers: {
          label: "Клиенты",
          trend: "Снижение",
          detail: "Привлечение",
        },
        accounts: {
          label: "Аккаунты",
          trend: "Удержание",
          detail: "Вовлечение",
        },
        growth: {
          label: "Темп роста",
          trend: "Стабильный рост",
          detail: "Соответствует прогнозу",
        },
      }}
    />,
  );

  expect(
    screen.getByRole("region", { name: "Показатели панели" }),
  ).toBeVisible();
  expect(screen.getByText("Рост")).toHaveClass("font-medium");
  expect(screen.getByText("Посетители")).toHaveClass("text-muted-foreground");
});
