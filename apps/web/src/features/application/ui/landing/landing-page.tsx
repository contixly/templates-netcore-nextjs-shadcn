import {
  IconApi,
  IconCookie,
  IconUsersGroup,
  IconWorld,
} from "@tabler/icons-react";
import Link from "next/link";
import { getTranslations } from "next-intl/server";

import { LandingFeatures } from "@/src/features/application/ui/landing/landing-features";
import { LandingFooter } from "@/src/features/application/ui/landing/landing-footer";
import { LandingHero } from "@/src/features/application/ui/landing/landing-hero";
import { ThemeSwitcher } from "@/src/features/application/ui/theme-switcher";
import { Button } from "@/src/components/ui/button";
import { applicationRoutes } from "@/src/features/application/application-routes";
import { authLoginUrl } from "@/src/features/authentication/sanitize-auth-redirect";

const featureIcons = [IconApi, IconWorld, IconCookie, IconUsersGroup] as const;
const featureIds = ["api", "web", "sessions", "workspaces"] as const;
const sourceHref =
  "https://github.com/contixly/templates-netcore-nextjs-shadcn";

export async function LandingPage() {
  const [t, navigation] = await Promise.all([
    getTranslations("application.landing"),
    getTranslations("application.shell.navigation"),
  ]);
  const loginHref = authLoginUrl(applicationRoutes.dashboard);
  const features = featureIds.map((id, index) => ({
    description: t(`features.${id}.description`),
    icon: featureIcons[index],
    title: t(`features.${id}.title`),
  }));

  return (
    <div className="flex min-h-svh min-w-0 flex-col bg-background">
      <header className="sticky top-0 z-20 border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/80">
        <div className="flex h-12 w-full items-center justify-between gap-3 px-3 md:px-4">
          <Link
            aria-label={t("brandHomeLabel")}
            className="flex min-w-0 items-center gap-2 font-semibold tracking-tight"
            href={applicationRoutes.home}
          >
            <span
              aria-hidden="true"
              className="grid size-6 shrink-0 place-items-center bg-foreground text-[0.6rem] font-bold text-background"
            >
              AT
            </span>
            <span className="truncate">{t("brand")}</span>
          </Link>
          <nav
            aria-label={t("navigationLabel")}
            className="flex shrink-0 items-center gap-1"
          >
            <Button asChild variant="outline">
              <Link href={loginHref}>{t("loginAction")}</Link>
            </Button>
            <Button asChild className="hidden sm:inline-flex" variant="outline">
              <Link href={applicationRoutes.docs}>
                {navigation("documentation")}
              </Link>
            </Button>
            <ThemeSwitcher />
          </nav>
        </div>
      </header>

      <main className="flex-1" id="main-content">
        <LandingHero
          description={t("description")}
          docsHref={applicationRoutes.docs}
          eyebrow={t("eyebrow")}
          loginHref={loginHref}
          primaryAction={t("primaryAction")}
          secondaryAction={t("secondaryAction")}
          securityNote={t("securityNote")}
          sourceAction={t("sourceAction")}
          sourceDescription={t("sourceDescription")}
          sourceHref={sourceHref}
          sourceTitle={t("sourceTitle")}
          title={t("title")}
        />
        <LandingFeatures
          description={t("featuresDescription")}
          features={features}
          loginHref={loginHref}
          title={t("featuresTitle")}
          valueAction={t("valueAction")}
          valueDescription={t("valueDescription")}
          valueEyebrow={t("valueEyebrow")}
          valueTitle={t("valueTitle")}
        />
      </main>

      <LandingFooter description={t("footerDescription")} text={t("footer")} />
    </div>
  );
}
