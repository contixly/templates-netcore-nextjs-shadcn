import { fireEvent, screen, within } from "@testing-library/react";

import { ActivityTable } from "@/src/features/dashboard/ui/activity-table";
import { dashboardRows } from "@/src/features/dashboard/dashboard-data";
import { renderWithMessages } from "@/test/support/render";

it("paginates the local fixture and opens an accessible edit drawer", () => {
  renderWithMessages(
    <ActivityTable initialRows={dashboardRows.slice(0, 12)} />,
  );

  expect(screen.getByText(/changes are not saved/i)).toBeInTheDocument();
  expect(screen.getByText("Page 1 of 2")).toBeVisible();

  fireEvent.click(screen.getByRole("button", { name: /next page/i }));

  expect(screen.getByText(/page 2/i)).toBeInTheDocument();

  fireEvent.click(screen.getByRole("button", { name: /previous page/i }));
  fireEvent.click(screen.getByRole("button", { name: /edit introduction/i }));

  expect(screen.getByRole("dialog", { name: /edit section/i })).toBeVisible();
  expect(screen.getByLabelText("Section title")).toHaveValue("Introduction");
});

it("owns deterministic selection, sorting, visibility, and reorder state", () => {
  renderWithMessages(
    <ActivityTable initialRows={dashboardRows.slice(0, 12)} />,
  );

  const introductionRow = screen.getByText("Introduction").closest("tr");
  expect(introductionRow).not.toBeNull();
  fireEvent.click(
    within(introductionRow!).getByRole("checkbox", {
      name: "Select Introduction",
    }),
  );
  expect(screen.getByText("1 of 12 row(s) selected.")).toBeVisible();

  fireEvent.click(screen.getByRole("button", { name: "Sort sections" }));
  expect(screen.getAllByRole("row")[1]).toHaveTextContent(
    "Adaptive Communication Protocols",
  );

  fireEvent.keyDown(screen.getByRole("button", { name: "Columns" }), {
    key: "Enter",
  });
  fireEvent.click(screen.getByRole("menuitemcheckbox", { name: "Type" }));
  expect(screen.queryByRole("columnheader", { name: "Type" })).toBeNull();

  fireEvent.click(
    screen.getByRole("button", {
      name: "Move Adaptive Communication Protocols down",
    }),
  );
  expect(screen.getAllByRole("row")[1]).toHaveTextContent(
    "Advanced Algorithms and Machine Learning",
  );
  expect(screen.getAllByRole("row")[2]).toHaveTextContent(
    "Adaptive Communication Protocols",
  );
});

it("reports selected and total rows from the filtered row models", () => {
  renderWithMessages(
    <ActivityTable initialRows={dashboardRows.slice(0, 12)} />,
  );

  const introductionRow = screen.getByText("Introduction").closest("tr");
  expect(introductionRow).not.toBeNull();
  fireEvent.click(
    within(introductionRow!).getByRole("checkbox", {
      name: "Select Introduction",
    }),
  );
  expect(screen.getByText("1 of 12 row(s) selected.")).toBeVisible();

  fireEvent.change(screen.getByRole("textbox", { name: "Search sections" }), {
    target: { value: "Technical approach" },
  });

  expect(screen.queryByText("Introduction")).not.toBeInTheDocument();
  expect(screen.getByText("Technical approach")).toBeVisible();
  expect(screen.getByText("0 of 1 row(s) selected.")).toBeVisible();

  fireEvent.change(screen.getByRole("textbox", { name: "Search sections" }), {
    target: { value: "" },
  });
  expect(screen.getByText("1 of 12 row(s) selected.")).toBeVisible();
});

it("reorders filtered matches only within their original full-order slots", () => {
  const rows = dashboardRows.slice(0, 5).map((row, index) => ({
    ...row,
    header: [
      "Match one",
      "Hidden alpha",
      "Match two",
      "Hidden beta",
      "Match three",
    ][index]!,
  }));
  renderWithMessages(<ActivityTable initialRows={rows} />);

  fireEvent.change(screen.getByRole("textbox", { name: "Search sections" }), {
    target: { value: "Match" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Move Match one down" }));
  fireEvent.change(screen.getByRole("textbox", { name: "Search sections" }), {
    target: { value: "" },
  });

  const displayedRows = screen.getAllByRole("row").slice(1);
  expect(displayedRows[0]).toHaveTextContent("Match two");
  expect(displayedRows[1]).toHaveTextContent("Hidden alpha");
  expect(displayedRows[2]).toHaveTextContent("Match one");
  expect(displayedRows[3]).toHaveTextContent("Hidden beta");
  expect(displayedRows[4]).toHaveTextContent("Match three");
});

it("keeps a single filtered row in place after a no-op move", () => {
  renderWithMessages(<ActivityTable initialRows={dashboardRows.slice(0, 5)} />);

  fireEvent.change(screen.getByRole("textbox", { name: "Search sections" }), {
    target: { value: "Technical approach" },
  });
  fireEvent.click(
    screen.getByRole("button", { name: "Move Technical approach down" }),
  );
  fireEvent.change(screen.getByRole("textbox", { name: "Search sections" }), {
    target: { value: "" },
  });

  const displayedRows = screen.getAllByRole("row").slice(1);
  expect(displayedRows[0]).toHaveTextContent("Introduction");
  expect(displayedRows[1]).toHaveTextContent("Table of contents");
  expect(displayedRows[2]).toHaveTextContent("Executive summary");
  expect(displayedRows[3]).toHaveTextContent("Technical approach");
  expect(displayedRows[4]).toHaveTextContent("Design");
});

it("switches between localized desktop tabs and exposes the mobile view selector", () => {
  renderWithMessages(
    <ActivityTable initialRows={dashboardRows.slice(0, 12)} />,
  );

  expect(screen.getByRole("tab", { name: "Outline" })).toHaveAttribute(
    "aria-selected",
    "true",
  );
  expect(
    screen.getByRole("combobox", { name: "Select table view" }),
  ).toBeVisible();
  fireEvent.click(screen.getByRole("tab", { name: "Past performance" }));
  expect(screen.queryByRole("table", { name: "Sections" })).toBeNull();
  expect(screen.getByRole("tabpanel")).toHaveTextContent(
    "No local demo content for this view.",
  );
});

it("applies edits only to local table state and makes no persistence claim", () => {
  const editedHeader = "Cost $$, match $&, before $`, after $'";
  renderWithMessages(<ActivityTable initialRows={dashboardRows.slice(0, 2)} />);

  fireEvent.click(screen.getByRole("button", { name: /edit introduction/i }));
  fireEvent.change(screen.getByLabelText("Section title"), {
    target: { value: editedHeader },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save changes" }));

  expect(screen.getByText(editedHeader)).toBeVisible();
  expect(
    screen.getByRole("checkbox", { name: `Select ${editedHeader}` }),
  ).toBeVisible();
  expect(
    screen.getByRole("button", { name: `Drag ${editedHeader} to reorder` }),
  ).toBeVisible();
  expect(
    screen.getByRole("button", { name: `Move ${editedHeader} down` }),
  ).toBeVisible();
  expect(
    screen.getByRole("button", { name: `Edit ${editedHeader}` }),
  ).toBeVisible();
  expect(screen.getByText(/changes are not saved/i)).toBeVisible();
  expect(dashboardRows[0]?.header).toBe("Introduction");
});

it("uses localized copy for every interactive table control", () => {
  renderWithMessages(
    <ActivityTable
      copy={{
        title: "Разделы",
        demoNotice: "Изменения не сохраняются.",
        search: "Поиск",
        empty: "Пусто",
        columns: "Столбцы",
        section: "Раздел",
        type: "Тип",
        status: "Статус",
        target: "Цель",
        limit: "Лимит",
        reviewer: "Проверяющий",
        actions: "Действия",
        sortSections: "Сортировать разделы",
        selectAll: "Выбрать все разделы",
        selectRow: "Выбрать {header}",
        dragRow: "Перетащить {header}",
        moveRow: "Переместить {header}",
        editRow: "Изменить {header}",
        edit: "Изменить",
        rowsSelected: "{selected} из {total}",
        page: "{current} из {total}",
        previousPage: "Назад",
        nextPage: "Вперёд",
        drawerTitle: "Сведения",
        editTitle: "Редактирование раздела",
        drawerDescription: "Демонстрационный раздел.",
        sectionTitle: "Название раздела",
        save: "Сохранить",
        cancel: "Отмена",
        localApplied: "Локальное изменение применено.",
        selectView: "Выберите представление таблицы",
        outline: "Структура",
        pastPerformance: "Предыдущие результаты",
        keyPersonnel: "Ключевые участники",
        focusDocuments: "Ключевые документы",
        emptyView: "Нет локальных демонстрационных данных.",
        rowHeaders: { "1": "Введение", "8": "Инновации и преимущества" },
        typeLabels: {
          "Cover page": "Титульная страница",
          Narrative: "Описание",
        },
        statusLabels: {
          Done: "Готово",
          "In Process": "В процессе",
        },
        assignReviewer: "Назначить проверяющего",
      }}
      initialRows={[dashboardRows[0]!, dashboardRows[7]!]}
    />,
  );

  expect(
    screen.getByRole("button", { name: "Сортировать разделы" }),
  ).toBeVisible();
  expect(
    screen.getByRole("checkbox", { name: "Выбрать Введение" }),
  ).toBeVisible();
  expect(
    screen.getByRole("button", { name: "Изменить Введение" }),
  ).toBeVisible();
  expect(screen.getByText("Титульная страница")).toBeVisible();
  expect(screen.getByText("В процессе")).toBeVisible();
  expect(screen.getByText("Назначить проверяющего")).toBeVisible();
});
