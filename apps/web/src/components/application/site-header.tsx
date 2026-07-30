import Link from "next/link";
import { useTranslations } from "next-intl";
import { Suspense } from "react";
import { connection } from "next/server";

import { ThemeSwitcher } from "@/src/components/application/theme-switcher";
import { OrganizationSwitcher } from "@/src/components/organizations/organization-switcher";
import { applicationRoutes } from "@/src/features/application/application-routes";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";

export async function OrganizationSwitcherRuntime() {
  await connection();
  const [session, organizations] = await Promise.all([
    loadServerAuthSession(),
    loadOrganizations(),
  ]);

  if (
    !session.ok ||
    session.data.authenticated !== true ||
    !session.data.session ||
    !organizations.ok ||
    organizations.data.items.length === 0
  ) {
    return null;
  }

  return (
    <OrganizationSwitcher
      activeOrganizationId={session.data.session.activeOrganizationId}
      nextCursor={organizations.data.nextCursor}
      organizations={organizations.data.items}
    />
  );
}

export function SiteHeader() {
  const t = useTranslations("common");

  return (
    <header className="border-b bg-background">
      <div className="mx-auto flex h-14 max-w-5xl items-center gap-6 px-4">
        <Link
          className="font-semibold tracking-tight"
          href={applicationRoutes.home}
        >
          {t("brand")}
        </Link>
        <nav aria-label={t("navigation.home")}>
          <Link
            className="text-sm text-muted-foreground hover:text-foreground"
            href={applicationRoutes.home}
          >
            {t("navigation.home")}
          </Link>
        </nav>
        <div className="mr-auto">
          <Suspense fallback={null}>
            <OrganizationSwitcherRuntime />
          </Suspense>
        </div>
        <ThemeSwitcher />
      </div>
    </header>
  );
}
