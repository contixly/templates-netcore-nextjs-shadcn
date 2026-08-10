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
  SelectGroup,
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
  const desktopGradientId = useId();
  const mobileGradientId = useId();
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
            className="hidden *:data-[slot=toggle-group-item]:!px-4 @[767px]/card:flex"
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
              className="flex w-40 **:data-[slot=select-value]:block **:data-[slot=select-value]:truncate @[767px]/card:hidden"
              size="sm"
            >
              <SelectValue />
            </SelectTrigger>
            <SelectContent className="rounded-xl">
              <SelectGroup>
                {rangeOptions.map((option) => (
                  <SelectItem
                    className="rounded-lg"
                    key={option.value}
                    value={option.value}
                  >
                    {copy[option.key]}
                  </SelectItem>
                ))}
              </SelectGroup>
            </SelectContent>
          </Select>
        </CardAction>
      </CardHeader>
      <CardContent className="px-2 pt-4 sm:px-6 sm:pt-6">
        <div
          aria-describedby={descriptionId}
          aria-label={copy.title}
          role="img"
        >
          <ChartContainer
            className="aspect-auto h-[250px] w-full"
            config={chartConfig}
          >
            <AreaChart accessibilityLayer data={data}>
              <defs>
                <linearGradient
                  id={desktopGradientId}
                  x1="0"
                  x2="0"
                  y1="0"
                  y2="1"
                >
                  <stop
                    offset="5%"
                    stopColor="var(--color-desktop)"
                    stopOpacity={1}
                  />
                  <stop
                    offset="95%"
                    stopColor="var(--color-desktop)"
                    stopOpacity={0.1}
                  />
                </linearGradient>
                <linearGradient
                  id={mobileGradientId}
                  x1="0"
                  x2="0"
                  y1="0"
                  y2="1"
                >
                  <stop
                    offset="5%"
                    stopColor="var(--color-mobile)"
                    stopOpacity={0.8}
                  />
                  <stop
                    offset="95%"
                    stopColor="var(--color-mobile)"
                    stopOpacity={0.1}
                  />
                </linearGradient>
              </defs>
              <CartesianGrid vertical={false} />
              <XAxis
                axisLine={false}
                dataKey="date"
                minTickGap={32}
                tickMargin={8}
                tickFormatter={formatDate}
                tickLine={false}
              />
              <YAxis hide />
              <ChartTooltip
                cursor={false}
                content={
                  <ChartTooltipContent
                    indicator="dot"
                    labelFormatter={(value) => formatDate(String(value))}
                  />
                }
              />
              <Area
                dataKey="mobile"
                fill={`url(#${mobileGradientId})`}
                stackId="activity"
                stroke="var(--color-mobile)"
                type="natural"
              />
              <Area
                dataKey="desktop"
                fill={`url(#${desktopGradientId})`}
                stackId="activity"
                stroke="var(--color-desktop)"
                type="natural"
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
