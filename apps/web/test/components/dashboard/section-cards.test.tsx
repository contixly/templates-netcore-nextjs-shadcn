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
        revenue: { label: "Выручка", detail: "Рост" },
        customers: { label: "Клиенты", detail: "Привлечение" },
        accounts: { label: "Аккаунты", detail: "Удержание" },
        growth: { label: "Темп роста", detail: "Стабильный рост" },
      }}
    />,
  );

  expect(
    screen.getByRole("region", { name: "Показатели панели" }),
  ).toBeVisible();
});
