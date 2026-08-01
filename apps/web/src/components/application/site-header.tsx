import Link from "next/link";
import { useTranslations } from "next-intl";
import type { ReactNode } from "react";

import { ThemeSwitcher } from "@/src/components/application/theme-switcher";
import { applicationRoutes } from "@/src/features/application/application-routes";

export function SiteHeader({
  accountNavigation = null,
  organizationSwitcher = null,
}: Readonly<{
  accountNavigation?: ReactNode;
  organizationSwitcher?: ReactNode;
}>) {
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
        <div className="mr-auto min-w-0 flex-1">{organizationSwitcher}</div>
        {accountNavigation}
        <div className="shrink-0">
          <ThemeSwitcher />
        </div>
      </div>
    </header>
  );
}
