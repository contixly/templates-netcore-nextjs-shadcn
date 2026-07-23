import Link from "next/link";
import { useTranslations } from "next-intl";

import { ThemeSwitcher } from "@/src/components/application/theme-switcher";
import { applicationRoutes } from "@/src/features/application/application-routes";

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
        <nav aria-label={t("navigation.home")} className="mr-auto">
          <Link
            className="text-sm text-muted-foreground hover:text-foreground"
            href={applicationRoutes.home}
          >
            {t("navigation.home")}
          </Link>
        </nav>
        <ThemeSwitcher />
      </div>
    </header>
  );
}
