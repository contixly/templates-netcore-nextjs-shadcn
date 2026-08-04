"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";

import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/src/components/ui/breadcrumb";
import { applicationRoutes } from "@/src/features/application/application-routes";
import { resolveApplicationPage } from "@/src/features/application/application-page-catalog";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";

function organizationKey(pathname: string): string | null {
  const encoded = /^\/w\/([^/]+)/.exec(pathname)?.[1];
  if (!encoded) {
    return null;
  }
  try {
    return decodeURIComponent(encoded);
  } catch {
    return encoded;
  }
}

function breadcrumbKey(pathname: string) {
  const page = resolveApplicationPage(pathname);
  if (!page) {
    return "settings";
  }
  if (page.id === "dashboard" || page.id === "organizationDashboard") {
    return "dashboard";
  }
  if (page.id === "welcome" || page.id === "workspaces") {
    return "workspaces";
  }
  if (page.id.startsWith("account")) {
    return "account";
  }
  return "settings";
}

export function ApplicationBreadcrumbs() {
  const pathname = usePathname();
  const t = useTranslations("application.shell");
  const pages = useTranslations("application.pages");
  const key = organizationKey(pathname);
  const homeHref = key
    ? organizationRoutes.dashboard(key)
    : applicationRoutes.dashboard;
  const current = breadcrumbKey(pathname);
  const page = resolveApplicationPage(pathname);
  const currentLabel =
    page?.id === "invitationDecision"
      ? pages("invitationDecision.title")
      : t(`breadcrumbs.${current}`);

  return (
    <Breadcrumb>
      <BreadcrumbList>
        <BreadcrumbItem className="hidden sm:inline-flex">
          <BreadcrumbLink asChild>
            <Link href={homeHref}>{t("breadcrumbs.home")}</Link>
          </BreadcrumbLink>
        </BreadcrumbItem>
        <BreadcrumbSeparator className="hidden sm:list-item" />
        <BreadcrumbItem>
          <BreadcrumbPage>{currentLabel}</BreadcrumbPage>
        </BreadcrumbItem>
      </BreadcrumbList>
    </Breadcrumb>
  );
}
