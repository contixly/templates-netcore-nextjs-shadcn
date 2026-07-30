import Link from "next/link";
import { useTranslations } from "next-intl";
import { connection } from "next/server";

import { ThemeSwitcher } from "@/src/components/application/theme-switcher";
import {
  OrganizationSwitcherRegistration,
  OrganizationSwitcherSlot,
  type OrganizationSwitcherContextValue,
} from "@/src/components/organizations/organization-switcher-context";
import { applicationRoutes } from "@/src/features/application/application-routes";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import { loadOrganization } from "@/src/lib/api/organizations/server/load-organization";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";

export async function OrganizationSwitcherRuntime({
  currentOrganization,
}: Readonly<{
  currentOrganization?: OrganizationSwitcherContextValue["currentOrganization"];
}> = {}) {
  await connection();
  const session = await loadServerAuthSession();

  if (
    !session.ok ||
    session.data.authenticated !== true ||
    !session.data.session
  ) {
    return null;
  }

  const organizations = await loadOrganizations();
  if (!organizations.ok || organizations.data.items.length === 0) {
    return null;
  }

  const items = organizations.data.items.map(({ canonicalKey, id, name }) => ({
    canonicalKey,
    id,
    name,
  }));
  const activeOrganizationId =
    session.data.session.activeOrganizationId ?? undefined;
  const activeIsInFirstPage = items.some(
    (organization) => organization.id === activeOrganizationId,
  );
  const activeOrganization =
    activeOrganizationId && !activeIsInFirstPage
      ? await loadOrganization(activeOrganizationId)
      : undefined;
  const activeCurrentOrganization =
    activeOrganization?.ok === true
      ? {
          canonicalKey: activeOrganization.data.canonicalKey,
          id: activeOrganization.data.id,
          name: activeOrganization.data.name,
        }
      : undefined;

  return (
    <OrganizationSwitcherRegistration
      activeOrganizationId={activeOrganizationId}
      currentOrganization={currentOrganization ?? activeCurrentOrganization}
      nextCursor={organizations.data.nextCursor}
      organizations={items}
    />
  );
}

export function SiteHeader() {
  const t = useTranslations("common");

  return (
    <header className="border-b bg-background">
      <div className="mx-auto flex h-14 max-w-5xl min-w-0 items-center gap-2 px-4 sm:gap-6">
        <Link
          className="shrink-0 font-semibold tracking-tight"
          href={applicationRoutes.home}
        >
          {t("brand")}
        </Link>
        <nav
          aria-label={t("navigation.home")}
          className="hidden shrink-0 sm:block"
        >
          <Link
            className="text-sm text-muted-foreground hover:text-foreground"
            href={applicationRoutes.home}
          >
            {t("navigation.home")}
          </Link>
        </nav>
        <div className="mr-auto min-w-0 flex-1">
          <OrganizationSwitcherSlot />
        </div>
        <div className="shrink-0">
          <ThemeSwitcher />
        </div>
      </div>
    </header>
  );
}
