import { IconTrendingDown, IconTrendingUp } from "@tabler/icons-react";

import { Badge } from "@/src/components/ui/badge";
import {
  Card,
  CardAction,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";

export type SectionCardsCopy = Readonly<{
  sectionLabel: string;
  revenue: Readonly<{ label: string; detail: string }>;
  customers: Readonly<{ label: string; detail: string }>;
  accounts: Readonly<{ label: string; detail: string }>;
  growth: Readonly<{ label: string; detail: string }>;
}>;

const defaultCopy: SectionCardsCopy = {
  sectionLabel: "Dashboard metrics",
  revenue: { label: "Total revenue", detail: "Trending up this month" },
  customers: {
    label: "New customers",
    detail: "Acquisition needs attention",
  },
  accounts: { label: "Active accounts", detail: "Strong user retention" },
  growth: { label: "Growth rate", detail: "Steady performance increase" },
};

const metrics = [
  {
    key: "revenue",
    value: "$1,250.00",
    change: "+12.5%",
    trend: "up",
  },
  { key: "customers", value: "1,234", change: "-20%", trend: "down" },
  { key: "accounts", value: "45,678", change: "+12.5%", trend: "up" },
  { key: "growth", value: "4.5%", change: "+4.5%", trend: "up" },
] as const;

export function SectionCards({
  copy = defaultCopy,
}: Readonly<{ copy?: SectionCardsCopy }>) {
  return (
    <section
      aria-label={copy.sectionLabel}
      className="grid grid-cols-1 gap-4 px-4 lg:grid-cols-2 lg:px-6 2xl:grid-cols-4"
    >
      {metrics.map((metric) => {
        const Icon = metric.trend === "up" ? IconTrendingUp : IconTrendingDown;
        const itemCopy = copy[metric.key];

        return (
          <Card className="@container/card" key={metric.key} role="article">
            <CardHeader>
              <CardDescription>{itemCopy.label}</CardDescription>
              <CardTitle className="text-2xl font-semibold tabular-nums">
                {metric.value}
              </CardTitle>
              <CardAction>
                <Badge variant="outline">
                  <Icon aria-hidden="true" />
                  {metric.change}
                </Badge>
              </CardAction>
            </CardHeader>
            <CardFooter className="flex-col items-start gap-1.5 text-sm">
              <div className="flex items-center gap-2 font-medium">
                {itemCopy.detail}
                <Icon aria-hidden="true" className="size-4" />
              </div>
            </CardFooter>
          </Card>
        );
      })}
    </section>
  );
}
