import { IconArrowRight, IconBook2 } from "@tabler/icons-react";
import type { Route } from "next";
import Link from "next/link";

import { Button } from "@/src/components/ui/button";

type LandingHeroProps = Readonly<{
  description: string;
  docsHref: Route;
  eyebrow: string;
  loginHref: Route;
  primaryAction: string;
  secondaryAction: string;
  title: string;
}>;

export function LandingHero({
  description,
  docsHref,
  eyebrow,
  loginHref,
  primaryAction,
  secondaryAction,
  title,
}: LandingHeroProps) {
  return (
    <section
      aria-labelledby="landing-title"
      className="relative isolate overflow-hidden border-b px-4 py-20 sm:px-6 md:py-28 lg:px-8"
    >
      <div
        aria-hidden="true"
        className="absolute inset-0 -z-10 bg-[radial-gradient(circle_at_top_left,color-mix(in_oklch,var(--primary)_12%,transparent),transparent_42%),linear-gradient(to_bottom,var(--background),color-mix(in_oklch,var(--muted)_45%,var(--background)))]"
      />
      <div className="mx-auto flex max-w-5xl flex-col items-start gap-6">
        <p className="border-l-2 border-primary pl-3 text-xs font-semibold tracking-[0.2em] text-muted-foreground uppercase">
          {eyebrow}
        </p>
        <h1
          className="max-w-4xl text-4xl font-semibold tracking-[-0.04em] text-balance sm:text-5xl md:text-7xl"
          id="landing-title"
        >
          {title}
        </h1>
        <p className="max-w-2xl text-base/7 text-pretty text-muted-foreground sm:text-lg/8">
          {description}
        </p>
        <div className="flex w-full flex-col gap-3 pt-2 sm:w-auto sm:flex-row">
          <Button asChild className="w-full sm:w-auto" size="lg">
            <Link href={loginHref}>
              {primaryAction}
              <IconArrowRight aria-hidden="true" />
            </Link>
          </Button>
          <Button
            asChild
            className="w-full sm:w-auto"
            size="lg"
            variant="outline"
          >
            <Link href={docsHref}>
              <IconBook2 aria-hidden="true" />
              {secondaryAction}
            </Link>
          </Button>
        </div>
      </div>
    </section>
  );
}
