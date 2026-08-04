import {
  ActivityChart,
  type ActivityChartCopy,
} from "@/src/components/dashboard/activity-chart";
import {
  ActivityTable,
  type ActivityTableCopy,
} from "@/src/components/dashboard/activity-table";
import {
  SectionCards,
  type SectionCardsCopy,
} from "@/src/components/dashboard/section-cards";
import { dashboardRows } from "@/src/features/dashboard/dashboard-data";

export type DashboardCopy = Readonly<{
  title: string;
  description: string;
  cards: SectionCardsCopy;
  chart: ActivityChartCopy;
  table: ActivityTableCopy;
}>;

export function DashboardPage({
  copy,
  organizationName,
}: Readonly<{
  copy: DashboardCopy;
  organizationName: string;
}>) {
  return (
    <div className="flex flex-col gap-6 py-6">
      <header className="flex flex-col gap-1 px-4 lg:px-6">
        <h1 className="text-2xl font-semibold tracking-tight">{copy.title}</h1>
        <p className="text-sm text-muted-foreground">
          {copy.description} <span className="sr-only">{organizationName}</span>
        </p>
      </header>
      <SectionCards copy={copy.cards} />
      <div className="px-4 lg:px-6">
        <ActivityChart copy={copy.chart} />
      </div>
      <ActivityTable copy={copy.table} initialRows={dashboardRows} />
    </div>
  );
}
