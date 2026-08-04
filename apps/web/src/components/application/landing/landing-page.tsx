import {
  IconApi,
  IconCookie,
  IconUsersGroup,
  IconWorld,
} from "@tabler/icons-react";
import Link from "next/link";
import { getTranslations } from "next-intl/server";

import { LandingFeatures } from "@/src/components/application/landing/landing-features";
import { LandingFooter } from "@/src/components/application/landing/landing-footer";
import { LandingHero } from "@/src/components/application/landing/landing-hero";
import { ThemeSwitcher } from "@/src/components/application/theme-switcher";
import { Button } from "@/src/components/ui/button";
import { applicationRoutes } from "@/src/features/application/application-routes";
import { authLoginUrl } from "@/src/features/authentication/sanitize-auth-redirect";

const featureIcons = [IconApi, IconWorld, IconCookie, IconUsersGroup] as const;
const featureIds = ["api", "web", "sessions", "workspaces"] as const;

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
    <div className="flex min-h-screen min-w-0 flex-col bg-background">
      <header className="border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/80">
        <div className="mx-auto flex h-14 max-w-6xl items-center justify-between gap-3 px-4 sm:px-6 lg:px-8">
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
            <Button asChild className="hidden sm:inline-flex" variant="ghost">
              <Link href={applicationRoutes.docs}>
                {navigation("documentation")}
              </Link>
            </Button>
            <Button asChild variant="outline">
              <Link href={loginHref}>{t("loginAction")}</Link>
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
          title={t("title")}
        />
        <LandingFeatures
          description={t("featuresDescription")}
          features={features}
          title={t("featuresTitle")}
          valueDescription={t("valueDescription")}
          valueEyebrow={t("valueEyebrow")}
          valueTitle={t("valueTitle")}
        />
      </main>

      <LandingFooter description={t("footerDescription")} text={t("footer")} />
    </div>
  );
}
