import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import { Skeleton } from "@/src/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/src/components/ui/table";

function DashboardCardSkeleton() {
  return (
    <Card className="@container/card" data-slot="dashboard-card-skeleton">
      <CardHeader>
        <CardDescription>
          <Skeleton className="h-4 w-24" />
        </CardDescription>
        <CardTitle>
          <Skeleton className="h-8 w-32" />
        </CardTitle>
        <CardAction>
          <Skeleton className="h-6 w-20" />
        </CardAction>
      </CardHeader>
      <CardFooter className="flex-col items-start gap-1.5">
        <Skeleton className="h-4 w-36" />
        <Skeleton className="h-4 w-44 max-w-full" />
      </CardFooter>
    </Card>
  );
}

function DashboardTableSkeleton() {
  return (
    <div className="min-w-0 px-4 lg:px-6" data-slot="dashboard-table-skeleton">
      <div className="flex min-w-0 flex-col gap-4">
        <div className="flex items-center justify-between gap-3">
          <Skeleton className="h-9 w-48" />
          <Skeleton className="h-9 w-28" />
        </div>
        <div className="overflow-hidden rounded-lg border">
          <Table>
            <TableHeader className="bg-muted">
              <TableRow>
                {Array.from({ length: 7 }, (_, index) => (
                  <TableHead key={index}>
                    <Skeleton className="h-4 w-20" />
                  </TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {Array.from({ length: 6 }, (_, rowIndex) => (
                <TableRow key={rowIndex}>
                  {Array.from({ length: 7 }, (_, cellIndex) => (
                    <TableCell key={cellIndex}>
                      <Skeleton className="h-4 w-24" />
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </div>
    </div>
  );
}

export function DashboardSkeleton({ label }: Readonly<{ label: string }>) {
  return (
    <div
      aria-busy="true"
      className="@container/main flex min-w-0 flex-col gap-6 py-6"
      data-slot="organization-dashboard-page-skeleton"
      role="status"
    >
      <span className="sr-only">{label}</span>
      <div className="flex flex-col gap-1 px-4 lg:px-6">
        <Skeleton className="h-7 w-56" />
        <Skeleton className="h-4 w-full max-w-md" />
      </div>
      <div className="grid grid-cols-1 gap-4 px-4 *:data-[slot=card]:rounded-lg *:data-[slot=card]:bg-gradient-to-t *:data-[slot=card]:from-primary/5 *:data-[slot=card]:to-card *:data-[slot=card]:shadow-xs lg:px-6 @xl/main:grid-cols-2 @5xl/main:grid-cols-4 dark:*:data-[slot=card]:bg-card">
        {Array.from({ length: 4 }, (_, index) => (
          <DashboardCardSkeleton key={index} />
        ))}
      </div>
      <div className="min-w-0 px-4 lg:px-6">
        <Card className="@container/card" data-slot="dashboard-chart-skeleton">
          <CardHeader>
            <CardTitle>
              <Skeleton className="h-5 w-40" />
            </CardTitle>
            <CardDescription>
              <Skeleton className="h-4 w-64 max-w-full" />
            </CardDescription>
            <CardAction>
              <Skeleton className="h-8 w-40" />
            </CardAction>
          </CardHeader>
          <CardContent className="px-2 pt-4 sm:px-6 sm:pt-6">
            <Skeleton className="h-[250px] w-full" />
          </CardContent>
        </Card>
      </div>
      <DashboardTableSkeleton />
    </div>
  );
}
