"use client";

import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import {
  type ColumnDef,
  type ColumnFiltersState,
  type Row,
  type RowSelectionState,
  type SortingState,
  type VisibilityState,
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable,
} from "@tanstack/react-table";
import {
  IconArrowsSort,
  IconChevronDown,
  IconChevronLeft,
  IconChevronRight,
  IconGripVertical,
} from "@tabler/icons-react";
import { useMemo, useState } from "react";
import { toast } from "sonner";

import { Badge } from "@/src/components/ui/badge";
import { Button } from "@/src/components/ui/button";
import { Checkbox } from "@/src/components/ui/checkbox";
import {
  Drawer,
  DrawerClose,
  DrawerContent,
  DrawerDescription,
  DrawerFooter,
  DrawerHeader,
  DrawerTitle,
} from "@/src/components/ui/drawer";
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuTrigger,
} from "@/src/components/ui/dropdown-menu";
import { Input } from "@/src/components/ui/input";
import { Label } from "@/src/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/src/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/src/components/ui/table";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/src/components/ui/tabs";
import { useIsMobile } from "@/src/hooks/use-mobile";
import type { DashboardRow } from "@/src/features/dashboard/dashboard-data";

export type ActivityTableCopy = Readonly<{
  title: string;
  demoNotice: string;
  search: string;
  empty: string;
  columns: string;
  section: string;
  type: string;
  status: string;
  target: string;
  limit: string;
  reviewer: string;
  actions: string;
  sortSections: string;
  selectAll: string;
  selectRow: string;
  dragRow: string;
  moveRow: string;
  editRow: string;
  edit: string;
  rowsSelected: string;
  page: string;
  previousPage: string;
  nextPage: string;
  drawerTitle: string;
  editTitle: string;
  drawerDescription: string;
  sectionTitle: string;
  save: string;
  cancel: string;
  localApplied: string;
  selectView: string;
  outline: string;
  pastPerformance: string;
  keyPersonnel: string;
  focusDocuments: string;
  emptyView: string;
  rowHeaders: Readonly<Record<string, string>>;
  typeLabels: Readonly<Record<string, string>>;
  statusLabels: Readonly<Record<string, string>>;
  assignReviewer: string;
}>;

const defaultCopy: ActivityTableCopy = {
  title: "Sections",
  demoNotice: "Demo changes are not saved.",
  search: "Search sections",
  empty: "No results.",
  columns: "Columns",
  section: "Section",
  type: "Type",
  status: "Status",
  target: "Target",
  limit: "Limit",
  reviewer: "Reviewer",
  actions: "Actions",
  sortSections: "Sort sections",
  selectAll: "Select all sections",
  selectRow: "Select {header}",
  dragRow: "Drag {header} to reorder",
  moveRow: "Move {header} down",
  editRow: "Edit {header}",
  edit: "Edit",
  rowsSelected: "{selected} of {total} row(s) selected.",
  page: "Page {current} of {total}",
  previousPage: "Go to previous page",
  nextPage: "Go to next page",
  drawerTitle: "Section details",
  editTitle: "Edit section",
  drawerDescription: "Review the selected demo section.",
  sectionTitle: "Section title",
  save: "Save changes",
  cancel: "Cancel",
  localApplied: "Local demo change applied. Changes are not saved.",
  selectView: "Select table view",
  outline: "Outline",
  pastPerformance: "Past performance",
  keyPersonnel: "Key personnel",
  focusDocuments: "Focus documents",
  emptyView: "No local demo content for this view.",
  rowHeaders: {},
  typeLabels: {},
  statusLabels: {},
  assignReviewer: "Assign reviewer",
};

function formatCopy(
  template: string,
  values: Readonly<Record<string, string | number>>,
) {
  return Object.entries(values).reduce(
    (result, [key, value]) =>
      result.replaceAll(`{${key}}`, () => String(value)),
    template,
  );
}

type EditableDashboardRow = Omit<DashboardRow, "status"> & { status: string };

function SortableActivityRow({
  copy,
  row,
  onEdit,
  onMoveDown,
}: Readonly<{
  copy: ActivityTableCopy;
  row: Row<EditableDashboardRow>;
  onEdit: (row: EditableDashboardRow) => void;
  onMoveDown: (id: number) => void;
}>) {
  const { attributes, listeners, setNodeRef, transform, transition } =
    useSortable({ id: row.original.id });

  return (
    <TableRow
      data-state={row.getIsSelected() ? "selected" : undefined}
      ref={setNodeRef}
      style={{ transform: CSS.Transform.toString(transform), transition }}
    >
      <TableCell>
        <Button
          {...attributes}
          {...listeners}
          aria-label={formatCopy(copy.dragRow, { header: row.original.header })}
          size="icon-xs"
          type="button"
          variant="ghost"
        >
          <IconGripVertical aria-hidden="true" />
        </Button>
      </TableCell>
      {row.getVisibleCells().map((cell) => (
        <TableCell key={cell.id}>
          {flexRender(cell.column.columnDef.cell, cell.getContext())}
        </TableCell>
      ))}
      <TableCell>
        <div className="flex items-center justify-end gap-1">
          <Button
            aria-label={formatCopy(copy.moveRow, {
              header: row.original.header,
            })}
            onClick={() => onMoveDown(row.original.id)}
            size="icon-xs"
            type="button"
            variant="ghost"
          >
            <IconChevronDown aria-hidden="true" />
          </Button>
          <Button
            aria-label={formatCopy(copy.editRow, {
              header: row.original.header,
            })}
            onClick={() => onEdit(row.original)}
            size="sm"
            type="button"
            variant="outline"
          >
            {copy.edit}
          </Button>
        </div>
      </TableCell>
    </TableRow>
  );
}

function SectionDrawer({
  copy,
  item,
  onClose,
  onSave,
}: Readonly<{
  copy: ActivityTableCopy;
  item: EditableDashboardRow | null;
  onClose: () => void;
  onSave: (header: string) => void;
}>) {
  const isMobile = useIsMobile();
  const [header, setHeader] = useState(item?.header ?? "");

  return (
    <Drawer
      direction={isMobile ? "bottom" : "right"}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      open={item !== null}
    >
      <DrawerContent>
        <form
          onSubmit={(event) => {
            event.preventDefault();
            onSave(header.trim());
          }}
        >
          <DrawerHeader>
            <DrawerTitle>{copy.editTitle}</DrawerTitle>
            <DrawerDescription>
              {copy.drawerTitle}. {copy.drawerDescription}
            </DrawerDescription>
          </DrawerHeader>
          <div className="flex flex-col gap-2 px-4">
            <Label htmlFor="dashboard-section-title">{copy.sectionTitle}</Label>
            <Input
              id="dashboard-section-title"
              onChange={(event) => setHeader(event.target.value)}
              value={header}
            />
          </div>
          <DrawerFooter>
            <Button disabled={header.trim().length === 0} type="submit">
              {copy.save}
            </Button>
            <DrawerClose asChild>
              <Button type="button" variant="outline">
                {copy.cancel}
              </Button>
            </DrawerClose>
          </DrawerFooter>
        </form>
      </DrawerContent>
    </Drawer>
  );
}

export function ActivityTable({
  copy = defaultCopy,
  initialRows,
}: Readonly<{
  copy?: ActivityTableCopy;
  initialRows: readonly DashboardRow[];
}>) {
  const [rows, setRows] = useState<readonly EditableDashboardRow[]>(() =>
    initialRows.map((row) => ({
      ...row,
      header: copy.rowHeaders[String(row.id)] ?? row.header,
      reviewer:
        row.reviewer === "Assign reviewer" ? copy.assignReviewer : row.reviewer,
      status: copy.statusLabels[row.status] ?? row.status,
      type: copy.typeLabels[row.type] ?? row.type,
    })),
  );
  const [sorting, setSorting] = useState<SortingState>([]);
  const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([]);
  const [columnVisibility, setColumnVisibility] = useState<VisibilityState>({});
  const [rowSelection, setRowSelection] = useState<RowSelectionState>({});
  const [editing, setEditing] = useState<EditableDashboardRow | null>(null);
  const [view, setView] = useState("outline");
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  );

  const columns = useMemo<ColumnDef<EditableDashboardRow>[]>(
    () => [
      {
        id: "select",
        header: ({ table }) => (
          <Checkbox
            aria-label={copy.selectAll}
            checked={
              table.getIsAllPageRowsSelected() ||
              (table.getIsSomePageRowsSelected() && "indeterminate")
            }
            onCheckedChange={(checked) =>
              table.toggleAllPageRowsSelected(Boolean(checked))
            }
          />
        ),
        cell: ({ row }) => (
          <Checkbox
            aria-label={formatCopy(copy.selectRow, {
              header: row.original.header,
            })}
            checked={row.getIsSelected()}
            onCheckedChange={(checked) => row.toggleSelected(Boolean(checked))}
          />
        ),
        enableHiding: false,
        enableSorting: false,
      },
      {
        accessorKey: "header",
        header: () => (
          <Button
            aria-label={copy.sortSections}
            onClick={() =>
              setSorting((current) =>
                current[0]?.id === "header" && current[0].desc === false
                  ? [{ id: "header", desc: true }]
                  : [{ id: "header", desc: false }],
              )
            }
            size="sm"
            type="button"
            variant="ghost"
          >
            {copy.section}
            <IconArrowsSort aria-hidden="true" />
          </Button>
        ),
      },
      { accessorKey: "type", header: copy.type },
      {
        accessorKey: "status",
        header: copy.status,
        cell: ({ row }) => (
          <Badge variant="outline">{row.original.status}</Badge>
        ),
      },
      { accessorKey: "target", header: copy.target },
      { accessorKey: "limit", header: copy.limit },
      { accessorKey: "reviewer", header: copy.reviewer },
    ],
    [copy],
  );

  // TanStack Table intentionally exposes stateful functions; do not memoize them.
  // eslint-disable-next-line react-hooks/incompatible-library
  const table = useReactTable({
    columns,
    data: rows as EditableDashboardRow[],
    enableRowSelection: true,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    getRowId: (row) => String(row.id),
    getSortedRowModel: getSortedRowModel(),
    initialState: { pagination: { pageIndex: 0, pageSize: 10 } },
    onColumnFiltersChange: setColumnFilters,
    onColumnVisibilityChange: setColumnVisibility,
    onRowSelectionChange: setRowSelection,
    onSortingChange: setSorting,
    state: { columnFilters, columnVisibility, rowSelection, sorting },
  });

  const applyDisplayedOrder = (
    reorder: (displayed: EditableDashboardRow[]) => EditableDashboardRow[],
  ) => {
    const displayed = table
      .getPrePaginationRowModel()
      .rows.map((row) => row.original);
    const reordered = reorder([...displayed]);
    const displayedIds = new Set(displayed.map((row) => row.id));
    setRows((current) => [
      ...reordered,
      ...current.filter((row) => !displayedIds.has(row.id)),
    ]);
    setSorting([]);
  };
  const moveRow = (id: number, offset: number) => {
    applyDisplayedOrder((displayed) => {
      const from = displayed.findIndex((row) => row.id === id);
      const to = Math.min(Math.max(from + offset, 0), displayed.length - 1);
      return from < 0 || from === to
        ? displayed
        : arrayMove(displayed, from, to);
    });
  };
  const handleDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id) return;
    applyDisplayedOrder((displayed) => {
      const from = displayed.findIndex((row) => row.id === active.id);
      const to = displayed.findIndex((row) => row.id === over.id);
      return from < 0 || to < 0 ? displayed : arrayMove(displayed, from, to);
    });
  };
  const saveEdit = (header: string) => {
    if (!editing || header.length === 0) return;
    setRows((current) =>
      current.map((row) =>
        row.id === editing.id ? Object.freeze({ ...row, header }) : row,
      ),
    );
    setEditing(null);
    toast.success(copy.localApplied);
  };
  const pageCount = Math.max(table.getPageCount(), 1);
  const viewOptions = [
    { value: "outline", label: copy.outline },
    { value: "past-performance", label: copy.pastPerformance },
    { value: "key-personnel", label: copy.keyPersonnel },
    { value: "focus-documents", label: copy.focusDocuments },
  ] as const;

  return (
    <section aria-labelledby="activity-table-title" className="px-4 lg:px-6">
      <Tabs onValueChange={setView} value={view}>
        <div className="mb-4 flex flex-col justify-between gap-3 sm:flex-row sm:items-end">
          <div className="flex flex-col gap-1">
            <h2 className="text-sm font-medium" id="activity-table-title">
              {copy.title}
            </h2>
            <p className="text-xs text-muted-foreground">{copy.demoNotice}</p>
          </div>
          <div className="flex justify-end">
            <Select onValueChange={setView} value={view}>
              <SelectTrigger
                aria-label={copy.selectView}
                className="w-full md:hidden"
              >
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {viewOptions.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <TabsList className="hidden md:flex">
              {viewOptions.map((option) => (
                <TabsTrigger
                  key={option.value}
                  onClick={() => setView(option.value)}
                  value={option.value}
                >
                  {option.label}
                </TabsTrigger>
              ))}
            </TabsList>
          </div>
        </div>
        <TabsContent value="outline">
          <div className="flex flex-col gap-4">
            <div className="flex flex-col justify-end gap-3 sm:flex-row sm:items-end">
              <div className="flex items-center gap-2">
                <Input
                  aria-label={copy.search}
                  className="max-w-56"
                  onChange={(event) =>
                    table
                      .getColumn("header")
                      ?.setFilterValue(event.target.value)
                  }
                  placeholder={copy.search}
                  value={
                    (table.getColumn("header")?.getFilterValue() as string) ??
                    ""
                  }
                />
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button type="button" variant="outline">
                      {copy.columns}
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    {table
                      .getAllColumns()
                      .filter((column) => column.getCanHide())
                      .map((column) => (
                        <DropdownMenuCheckboxItem
                          checked={column.getIsVisible()}
                          key={column.id}
                          onCheckedChange={(checked) =>
                            column.toggleVisibility(Boolean(checked))
                          }
                        >
                          {column.id === "header"
                            ? copy.section
                            : column.id === "type"
                              ? copy.type
                              : column.id === "status"
                                ? copy.status
                                : column.id === "target"
                                  ? copy.target
                                  : column.id === "limit"
                                    ? copy.limit
                                    : copy.reviewer}
                        </DropdownMenuCheckboxItem>
                      ))}
                  </DropdownMenuContent>
                </DropdownMenu>
              </div>
            </div>

            <DndContext
              collisionDetection={closestCenter}
              id="dashboard-sections"
              onDragEnd={handleDragEnd}
              sensors={sensors}
            >
              <Table aria-label={copy.title}>
                <TableHeader>
                  {table.getHeaderGroups().map((headerGroup) => (
                    <TableRow key={headerGroup.id}>
                      <TableHead aria-label={copy.sortSections} />
                      {headerGroup.headers.map((header) => (
                        <TableHead key={header.id}>
                          {header.isPlaceholder
                            ? null
                            : flexRender(
                                header.column.columnDef.header,
                                header.getContext(),
                              )}
                        </TableHead>
                      ))}
                      <TableHead>{copy.actions}</TableHead>
                    </TableRow>
                  ))}
                </TableHeader>
                <TableBody>
                  <SortableContext
                    items={table
                      .getRowModel()
                      .rows.map((row) => row.original.id)}
                    strategy={verticalListSortingStrategy}
                  >
                    {table.getRowModel().rows.length > 0 ? (
                      table
                        .getRowModel()
                        .rows.map((row) => (
                          <SortableActivityRow
                            copy={copy}
                            key={row.id}
                            onEdit={setEditing}
                            onMoveDown={(id) => moveRow(id, 1)}
                            row={row}
                          />
                        ))
                    ) : (
                      <TableRow>
                        <TableCell className="h-24 text-center" colSpan={9}>
                          {copy.empty}
                        </TableCell>
                      </TableRow>
                    )}
                  </SortableContext>
                </TableBody>
              </Table>
            </DndContext>

            <div className="flex flex-col justify-between gap-3 text-xs sm:flex-row sm:items-center">
              <p>
                {formatCopy(copy.rowsSelected, {
                  selected: Object.keys(rowSelection).length,
                  total: rows.length,
                })}
              </p>
              <div className="flex items-center gap-2">
                <span>
                  {formatCopy(copy.page, {
                    current: table.getState().pagination.pageIndex + 1,
                    total: pageCount,
                  })}
                </span>
                <Button
                  aria-label={copy.previousPage}
                  disabled={!table.getCanPreviousPage()}
                  onClick={() => table.previousPage()}
                  size="icon-sm"
                  type="button"
                  variant="outline"
                >
                  <IconChevronLeft aria-hidden="true" />
                </Button>
                <Button
                  aria-label={copy.nextPage}
                  disabled={!table.getCanNextPage()}
                  onClick={() => table.nextPage()}
                  size="icon-sm"
                  type="button"
                  variant="outline"
                >
                  <IconChevronRight aria-hidden="true" />
                </Button>
              </div>
            </div>
          </div>
        </TabsContent>
        {viewOptions.slice(1).map((option) => (
          <TabsContent key={option.value} value={option.value}>
            <p className="py-8 text-sm text-muted-foreground">
              {copy.emptyView}
            </p>
          </TabsContent>
        ))}
      </Tabs>
      {editing ? (
        <SectionDrawer
          copy={copy}
          item={editing}
          key={editing.id}
          onClose={() => setEditing(null)}
          onSave={saveEdit}
        />
      ) : null}
    </section>
  );
}
