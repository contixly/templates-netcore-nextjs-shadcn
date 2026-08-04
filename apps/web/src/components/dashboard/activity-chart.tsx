"use client";

import { useId, useMemo, useState } from "react";
import { Area, AreaChart, CartesianGrid, XAxis, YAxis } from "recharts";

import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/src/components/ui/chart";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/src/components/ui/select";
import { ToggleGroup, ToggleGroupItem } from "@/src/components/ui/toggle-group";
import { useIsMobile } from "@/src/hooks/use-mobile";
import { activityPoints } from "@/src/features/dashboard/dashboard-data";
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";

type ActivityRange = "90" | "30" | "7";

export type ActivityChartCopy = Readonly<{
  title: string;
  description: string;
  locale: string;
  last90Days: string;
  last30Days: string;
  last7Days: string;
  desktop: string;
  mobile: string;
}>;

const defaultCopy: ActivityChartCopy = {
  title: "Total visitors",
  description: "Chart values for the selected period.",
  locale: "en",
  last90Days: "Last 3 months",
  last30Days: "Last 30 days",
  last7Days: "Last 7 days",
  desktop: "Desktop",
  mobile: "Mobile",
};

const rangeOptions = [
  { value: "90", key: "last90Days" },
  { value: "30", key: "last30Days" },
  { value: "7", key: "last7Days" },
] as const satisfies readonly {
  value: ActivityRange;
  key: keyof ActivityChartCopy;
}[];

export function ActivityChart({
  copy = defaultCopy,
}: Readonly<{ copy?: ActivityChartCopy }>) {
  const isMobile = useIsMobile();
  const descriptionId = useId();
  const [selectedRange, setSelectedRange] = useState<ActivityRange | null>(
    null,
  );
  const range = selectedRange ?? (isMobile ? "7" : "90");

  const data = useMemo(
    () => activityPoints.slice(-Number.parseInt(range, 10)),
    [range],
  );
  const chartConfig = useMemo(
    () =>
      ({
        desktop: { label: copy.desktop, color: "var(--chart-1)" },
        mobile: { label: copy.mobile, color: "var(--chart-2)" },
      }) satisfies ChartConfig,
    [copy.desktop, copy.mobile],
  );
  const selectRange = (value: string) => {
    if (value === "90" || value === "30" || value === "7") {
      setSelectedRange(value);
    }
  };
  const formatDate = (value: string) =>
    new Intl.DateTimeFormat(copy.locale, {
      day: "numeric",
      month: "short",
      timeZone: "UTC",
    }).format(new Date(`${value}T00:00:00Z`));

  return (
    <Card className="@container/card">
      <CardHeader>
        <CardTitle>{copy.title}</CardTitle>
        <CardDescription>
          {rangeOptions.find((option) => option.value === range)?.key
            ? copy[rangeOptions.find((option) => option.value === range)!.key]
            : copy.last90Days}
        </CardDescription>
        <CardAction>
          <ToggleGroup
            aria-label={copy.title}
            className="hidden sm:flex"
            onValueChange={selectRange}
            type="single"
            value={range}
            variant="outline"
          >
            {rangeOptions.map((option) => (
              <ToggleGroupItem
                aria-label={copy[option.key]}
                key={option.value}
                value={option.value}
              >
                {copy[option.key]}
              </ToggleGroupItem>
            ))}
          </ToggleGroup>
          <Select onValueChange={selectRange} value={range}>
            <SelectTrigger
              aria-label={copy.title}
              className="w-40 sm:hidden"
              size="sm"
            >
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {rangeOptions.map((option) => (
                <SelectItem key={option.value} value={option.value}>
                  {copy[option.key]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </CardAction>
      </CardHeader>
      <CardContent>
        <div
          aria-describedby={descriptionId}
          aria-label={copy.title}
          role="img"
        >
          <ChartContainer className="h-64 w-full" config={chartConfig}>
            <AreaChart accessibilityLayer data={data}>
              <CartesianGrid vertical={false} />
              <XAxis
                dataKey="date"
                minTickGap={28}
                tickFormatter={formatDate}
                tickLine={false}
              />
              <YAxis hide />
              <ChartTooltip
                content={
                  <ChartTooltipContent
                    labelFormatter={(value) => formatDate(String(value))}
                  />
                }
              />
              <Area
                dataKey="desktop"
                fill="var(--color-desktop)"
                fillOpacity={0.3}
                stroke="var(--color-desktop)"
                type="monotone"
              />
              <Area
                dataKey="mobile"
                fill="var(--color-mobile)"
                fillOpacity={0.15}
                stroke="var(--color-mobile)"
                type="monotone"
              />
            </AreaChart>
          </ChartContainer>
        </div>
        <p
          className="mt-2 text-xs text-muted-foreground"
          data-testid="activity-chart-visible-range"
        >
          {data[0] ? formatDate(data[0].date) : ""} –{" "}
          {data.at(-1) ? formatDate(data.at(-1)!.date) : ""}
        </p>
        <div className="sr-only" id={descriptionId}>
          <p>{copy.description}</p>
          <ul>
            {data.map((point) => (
              <li data-testid="activity-chart-point" key={point.date}>
                {formatDate(point.date)}: {copy.desktop} {point.desktop};{" "}
                {copy.mobile} {point.mobile}
              </li>
            ))}
          </ul>
        </div>
      </CardContent>
    </Card>
  );
}
