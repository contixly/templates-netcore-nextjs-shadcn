import Link from "next/link";
import { useTranslations } from "next-intl";
import { IconBooks } from "@tabler/icons-react";
import type { CSSProperties, ReactNode } from "react";

import { ThemeSwitcher } from "@/src/features/application/ui/theme-switcher";
import { Button } from "@/src/components/ui/button";
import { applicationRoutes } from "@/src/features/application/application-routes";

export function SiteHeader({
  accountNavigation = null,
  organizationSwitcher = null,
}: Readonly<{
  accountNavigation?: ReactNode;
  organizationSwitcher?: ReactNode;
}>) {
  const t = useTranslations("common");
  const navigation = useTranslations("application.shell.navigation");

  return (
    <header
      className="sticky top-0 z-20 flex h-(--header-height) shrink-0 items-center border-b bg-background transition-[width,height] ease-linear"
      style={
        { "--header-height": "calc(var(--spacing) * 12)" } as CSSProperties
      }
    >
      <div className="flex w-full min-w-0 items-center gap-2 pr-2 pl-3 md:pl-4 lg:gap-4">
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
        <div className="flex shrink-0 items-center gap-2">
          <Button asChild size="icon" variant="outline">
            <Link
              aria-label={navigation("documentation")}
              href={applicationRoutes.docs}
              title={navigation("documentation")}
            >
              <IconBooks aria-hidden="true" />
            </Link>
          </Button>
          <ThemeSwitcher />
        </div>
      </div>
    </header>
  );
}
