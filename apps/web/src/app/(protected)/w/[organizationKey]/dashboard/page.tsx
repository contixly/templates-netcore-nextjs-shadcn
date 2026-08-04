import { forbidden, redirect } from "next/navigation";
import { connection } from "next/server";
import { getLocale, getTranslations } from "next-intl/server";

import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { OrganizationOnboarding } from "@/src/components/organizations/organization-onboarding";
import {
  DashboardPage,
  type DashboardCopy,
} from "@/src/components/dashboard/dashboard-page";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { dashboardRoutes } from "@/src/features/dashboard/dashboard-routes";
import { dashboardRows } from "@/src/features/dashboard/dashboard-data";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";
import { buildApplicationPageMetadata } from "@/src/lib/metadata";

type OrganizationDashboardPageProps = Readonly<{
  params: Promise<{ organizationKey: string }>;
}>;

export function generateMetadata() {
  return buildApplicationPageMetadata("organizationDashboard");
}

export default async function OrganizationDashboardPage({
  params,
}: OrganizationDashboardPageProps) {
  await connection();
  const { organizationKey } = await params;
  const route = dashboardRoutes.organization(organizationKey);
  const sessionPromise = loadProtectedSession(route);
  const organizationPromise = loadOrganization(organizationKey);
  const organizationsPromise = loadOrganizations();
  const translationsPromise = getTranslations("organizations.pages.dashboard");
  const session = await sessionPromise;

  if (!session.ok) {
    return <OrganizationFailure failure={session.failure} />;
  }
  if (
    session.data.authenticated !== true ||
    !session.data.session ||
    !session.data.user
  ) {
    return (
      <OrganizationFailure
        failure={{ kind: "network", code: "api_unavailable" }}
      />
    );
  }
  const organization = await organizationPromise;
  if (!organization.ok) {
    if (
      organization.failure.kind === "problem" &&
      organization.failure.status === 404
    ) {
      const organizations = await organizationsPromise;
      if (!organizations.ok) {
        return <OrganizationFailure failure={organizations.failure} />;
      }
      if (organizations.data.items.length === 0) {
        return <OrganizationOnboarding />;
      }
      forbidden();
    }
    return <OrganizationFailure failure={organization.failure} />;
  }
  if (organizationKey !== organization.data.canonicalKey) {
    redirect(dashboardRoutes.organization(organization.data.canonicalKey));
  }
  const organizationT = await translationsPromise;
  const dashboardT = await getTranslations("dashboard");
  const locale = await getLocale();

  const translated = (key: string, fallback: string) => {
    const value = dashboardT(key as never);
    return value === key ? fallback : value;
  };
  const translatedTemplate = (
    key: string,
    fallback: string,
    values: Readonly<Record<string, string>>,
  ) => {
    const value = dashboardT(key as never, values as never);
    return value === key ? fallback : value;
  };
  const copy: DashboardCopy = {
    title: translated("pages.organization.title", organizationT("title")),
    description: translated(
      "pages.organization.description",
      organizationT("description"),
    ),
    cards: {
      sectionLabel: translated("cards.sectionLabel", "Dashboard metrics"),
      revenue: {
        label: translated("cards.revenue.label", "Total revenue"),
        detail: translated("cards.revenue.detail", "Trending up this month"),
      },
      customers: {
        label: translated("cards.customers.label", "New customers"),
        detail: translated(
          "cards.customers.detail",
          "Acquisition needs attention",
        ),
      },
      accounts: {
        label: translated("cards.accounts.label", "Active accounts"),
        detail: translated("cards.accounts.detail", "Strong user retention"),
      },
      growth: {
        label: translated("cards.growth.label", "Growth rate"),
        detail: translated(
          "cards.growth.detail",
          "Steady performance increase",
        ),
      },
    },
    chart: {
      title: translated("ranges.title", "Total visitors"),
      description: translated(
        "ranges.description",
        "Chart values for the selected period.",
      ),
      locale,
      last90Days: translated("ranges.last90Days", "Last 3 months"),
      last30Days: translated("ranges.last30Days", "Last 30 days"),
      last7Days: translated("ranges.last7Days", "Last 7 days"),
      desktop: translated("ranges.desktop", "Desktop"),
      mobile: translated("ranges.mobile", "Mobile"),
    },
    table: {
      title: translated("table.title", "Sections"),
      demoNotice: translated("table.demoNotice", "Demo changes are not saved."),
      search: translated("table.search", "Search sections"),
      empty: translated("table.empty", "No results."),
      columns: translated("table.columns", "Columns"),
      section: translated("table.section", "Section"),
      type: translated("table.type", "Type"),
      status: translated("table.status", "Status"),
      target: translated("table.target", "Target"),
      limit: translated("table.limit", "Limit"),
      reviewer: translated("table.reviewer", "Reviewer"),
      actions: translated("table.actions", "Actions"),
      sortSections: translated("table.sortSections", "Sort sections"),
      selectAll: translated("table.selectAll", "Select all sections"),
      selectRow: translatedTemplate("table.selectRow", "Select {header}", {
        header: "{header}",
      }),
      dragRow: translatedTemplate("table.dragRow", "Drag {header} to reorder", {
        header: "{header}",
      }),
      moveRow: translatedTemplate("table.moveRow", "Move {header} down", {
        header: "{header}",
      }),
      editRow: translatedTemplate("table.editRow", "Edit {header}", {
        header: "{header}",
      }),
      edit: translated("table.edit", "Edit"),
      rowsSelected: translatedTemplate(
        "table.rowsSelected",
        "{selected} of {total} row(s) selected.",
        { selected: "{selected}", total: "{total}" },
      ),
      page: translatedTemplate("table.page", "Page {current} of {total}", {
        current: "{current}",
        total: "{total}",
      }),
      previousPage: translated("table.previousPage", "Go to previous page"),
      nextPage: translated("table.nextPage", "Go to next page"),
      drawerTitle: translated("drawer.title", "Section details"),
      editTitle: translated("drawer.editTitle", "Edit section"),
      drawerDescription: translated(
        "drawer.description",
        "Review the selected demo section.",
      ),
      sectionTitle: translated("table.sectionTitle", "Section title"),
      save: translated("drawer.save", "Save changes"),
      cancel: translated("drawer.cancel", "Cancel"),
      localApplied: translated(
        "table.localApplied",
        "Local demo change applied. Changes are not saved.",
      ),
      selectView: translated("table.selectView", "Select table view"),
      outline: translated("table.outline", "Outline"),
      pastPerformance: translated("table.pastPerformance", "Past performance"),
      keyPersonnel: translated("table.keyPersonnel", "Key personnel"),
      focusDocuments: translated("table.focusDocuments", "Focus documents"),
      emptyView: translated(
        "table.emptyView",
        "No local demo content for this view.",
      ),
      rowHeaders: Object.fromEntries(
        dashboardRows.map((row) => [
          String(row.id),
          translated(`table.rows.${row.id}`, row.header),
        ]),
      ),
      typeLabels: {
        "Cover page": translated("table.types.coverPage", "Cover page"),
        "Table of contents": translated(
          "table.types.tableOfContents",
          "Table of contents",
        ),
        Narrative: translated("table.types.narrative", "Narrative"),
        "Technical content": translated(
          "table.types.technicalContent",
          "Technical content",
        ),
        "Plain language": translated(
          "table.types.plainLanguage",
          "Plain language",
        ),
        Legal: translated("table.types.legal", "Legal"),
        Visual: translated("table.types.visual", "Visual"),
        Financial: translated("table.types.financial", "Financial"),
        Research: translated("table.types.research", "Research"),
        Planning: translated("table.types.planning", "Planning"),
      },
      statusLabels: {
        Done: translated("table.statuses.done", "Done"),
        "In Process": translated("table.statuses.inProcess", "In Process"),
      },
      assignReviewer: translated("table.assignReviewer", "Assign reviewer"),
    },
  };

  return (
    <DashboardPage copy={copy} organizationName={organization.data.name} />
  );
}
