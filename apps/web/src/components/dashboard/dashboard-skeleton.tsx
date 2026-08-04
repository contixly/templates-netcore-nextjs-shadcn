import {
  Card,
  CardContent,
  CardFooter,
  CardHeader,
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

export function DashboardSkeleton({ label }: Readonly<{ label: string }>) {
  return (
    <div aria-busy="true" className="flex flex-col gap-6 py-6" role="status">
      <span className="sr-only">{label}</span>
      <div className="flex flex-col gap-1 px-4 lg:px-6">
        <Skeleton className="h-7 w-56" />
        <Skeleton className="h-4 w-full max-w-md" />
      </div>
      <div className="grid grid-cols-1 gap-4 px-4 lg:grid-cols-2 lg:px-6 2xl:grid-cols-4">
        {Array.from({ length: 4 }, (_, index) => (
          <Card data-slot="dashboard-card-skeleton" key={index}>
            <CardHeader>
              <Skeleton className="h-4 w-24" />
              <Skeleton className="h-8 w-32" />
            </CardHeader>
            <CardFooter>
              <Skeleton className="h-4 w-40" />
            </CardFooter>
          </Card>
        ))}
      </div>
      <div className="px-4 lg:px-6">
        <Card data-slot="dashboard-chart-skeleton">
          <CardHeader>
            <Skeleton className="h-5 w-40" />
            <Skeleton className="h-4 w-28" />
          </CardHeader>
          <CardContent>
            <Skeleton className="h-64 w-full" />
          </CardContent>
        </Card>
      </div>
      <div data-slot="dashboard-table-skeleton" className="px-4 lg:px-6">
        <div className="mb-4 flex items-center justify-between gap-3">
          <Skeleton className="h-9 w-48" />
          <Skeleton className="h-9 w-28" />
        </div>
        <Table>
          <TableHeader>
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
  );
}
