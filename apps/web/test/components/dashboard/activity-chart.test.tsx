import { fireEvent, screen, waitFor, within } from "@testing-library/react";

import { ActivityChart } from "@/src/components/dashboard/activity-chart";
import { useIsMobile } from "@/src/hooks/use-mobile";
import { renderWithMessages } from "@/test/support/render";
import { mockDashboardGeometry } from "@/test/support/dashboard-geometry";

jest.mock("@/src/hooks/use-mobile", () => ({
  useIsMobile: jest.fn(),
}));

const mockedUseIsMobile = jest.mocked(useIsMobile);
let restoreGeometry: () => void;

beforeEach(() => {
  restoreGeometry = mockDashboardGeometry();
  mockedUseIsMobile.mockReturnValue(false);
});

afterEach(() => restoreGeometry());

it("lets the user display the latest thirty immutable activity points", () => {
  renderWithMessages(<ActivityChart />);

  fireEvent.click(screen.getByRole("radio", { name: "Last 30 days" }));

  expect(screen.getAllByTestId("activity-chart-point")).toHaveLength(30);
  expect(
    screen.getByRole("img", { name: "Total visitors" }),
  ).toBeInTheDocument();
});

it("uses the seven-day range initially on mobile", async () => {
  mockedUseIsMobile.mockReturnValue(true);

  renderWithMessages(<ActivityChart />);

  await waitFor(() => {
    expect(screen.getAllByTestId("activity-chart-point")).toHaveLength(7);
  });
  expect(screen.getByRole("radio", { name: "Last 7 days" })).toHaveAttribute(
    "data-state",
    "on",
  );
});

it("formats visible dates and exposes plotted values outside the image subtree", () => {
  const locale = "ru";
  const formatter = new Intl.DateTimeFormat(locale, {
    day: "numeric",
    month: "short",
    timeZone: "UTC",
  });
  const firstDate = formatter.format(new Date("2024-06-01T00:00:00Z"));

  renderWithMessages(
    <ActivityChart
      copy={{
        title: "Всего посетителей",
        description: "Значения графика за выбранный период.",
        locale,
        last90Days: "Последние 3 месяца",
        last30Days: "Последние 30 дней",
        last7Days: "Последние 7 дней",
        desktop: "Компьютер",
        mobile: "Телефон",
      }}
    />,
  );
  fireEvent.click(screen.getByRole("radio", { name: "Последние 30 дней" }));

  const chart = screen.getByRole("img", { name: "Всего посетителей" });
  const descriptionId = chart.getAttribute("aria-describedby");
  expect(descriptionId).toBeTruthy();
  const description = document.getElementById(descriptionId!);
  expect(description).not.toBeNull();
  expect(chart).not.toContainElement(description);
  expect(within(description!).getByText(/Значения графика/)).toBeVisible();
  expect(within(description!).getAllByRole("listitem")[0]).toHaveTextContent(
    firstDate,
  );
  expect(screen.getByTestId("activity-chart-visible-range")).toHaveTextContent(
    firstDate,
  );
});
