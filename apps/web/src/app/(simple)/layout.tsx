import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { IconBooks, IconHome } from "@tabler/icons-react";
import type { ReactNode } from "react";

import { Button } from "@/src/components/ui/button";
import { ThemeSwitcher } from "@/src/features/application/ui/theme-switcher";
import { applicationRoutes } from "@/src/features/application/application-routes";

export const metadata: Metadata = {
  robots: { index: false, follow: false },
  alternates: { canonical: null },
  openGraph: { url: null },
};

export default async function SimpleLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  const [common, navigation] = await Promise.all([
    getTranslations("common"),
    getTranslations("application.shell.navigation"),
  ]);

  return (
    <div className="flex min-h-svh flex-col bg-muted [&>main]:min-h-0 [&>main]:flex-1">
      <header className="sticky top-0 z-20 flex h-12 shrink-0 items-center bg-muted transition-[width,height] ease-linear">
        <div className="flex w-full items-center gap-1 pr-2 pl-1 md:pl-4 lg:gap-2 lg:pl-4">
          <div className="ms-auto flex items-center gap-2">
            <Button asChild size="icon" variant="outline">
              <Link
                aria-label={common("actions.home")}
                href={applicationRoutes.home}
                title={common("actions.home")}
              >
                <IconHome aria-hidden="true" />
              </Link>
            </Button>
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
      {children}
    </div>
  );
}
