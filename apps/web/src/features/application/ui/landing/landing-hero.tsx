import {
  IconArrowRight,
  IconBrandGithub,
  IconExternalLink,
  IconLock,
  IconTerminal,
} from "@tabler/icons-react";
import type { Route } from "next";
import Link from "next/link";

import { Badge } from "@/src/components/ui/badge";
import { Button } from "@/src/components/ui/button";

type LandingHeroProps = Readonly<{
  description: string;
  docsHref: Route;
  eyebrow: string;
  loginHref: Route;
  primaryAction: string;
  secondaryAction: string;
  securityNote: string;
  sourceAction: string;
  sourceDescription: string;
  sourceHref: string;
  sourceTitle: string;
  title: string;
}>;

export function LandingHero({
  description,
  docsHref,
  eyebrow,
  loginHref,
  primaryAction,
  secondaryAction,
  securityNote,
  sourceAction,
  sourceDescription,
  sourceHref,
  sourceTitle,
  title,
}: LandingHeroProps) {
  return (
    <section
      aria-labelledby="landing-title"
      className="relative isolate flex flex-col items-center gap-8 overflow-hidden border-b px-4 py-20 text-center md:px-6 md:py-32"
    >
      <div
        aria-hidden="true"
        className="absolute inset-0 -z-10 bg-[radial-gradient(ellipse_at_top,color-mix(in_oklch,var(--primary)_10%,var(--background))_0%,transparent_70%)]"
      />
      <div className="relative flex w-full max-w-3xl min-w-0 flex-col items-center gap-6">
        <Badge className="gap-1.5" variant="outline">
          <IconTerminal aria-hidden="true" className="size-3" />
          {eyebrow}
        </Badge>

        <h1
          className="w-full text-3xl font-bold tracking-tight text-pretty md:text-5xl"
          id="landing-title"
        >
          {title}
        </h1>

        <p className="w-full max-w-xl text-sm leading-relaxed text-muted-foreground md:text-base">
          {description}
        </p>

        <Button asChild size="lg">
          <Link href={loginHref}>
            {primaryAction}
            <IconArrowRight aria-hidden="true" />
          </Link>
        </Button>

        <div className="grid w-full max-w-xl gap-3 border border-border/70 bg-background/80 px-4 py-4 text-left shadow-sm backdrop-blur-sm sm:grid-cols-[1fr_auto] sm:items-center">
          <div className="min-w-0 space-y-1">
            <div className="flex min-w-0 items-center gap-2">
              <IconBrandGithub
                aria-hidden="true"
                className="size-4 shrink-0 text-foreground"
              />
              <span className="truncate text-sm font-medium">
                {sourceTitle}
              </span>
            </div>
            <p className="text-xs leading-relaxed text-pretty text-muted-foreground">
              {sourceDescription}
            </p>
          </div>
          <Button
            asChild
            className="w-full sm:w-auto"
            size="sm"
            variant="outline"
          >
            <a href={sourceHref} rel="noreferrer" target="_blank">
              {sourceAction}
              <IconExternalLink aria-hidden="true" className="size-3.5" />
            </a>
          </Button>
        </div>

        <p className="w-full max-w-xl text-xs leading-relaxed text-muted-foreground">
          <IconLock
            aria-hidden="true"
            className="mr-1.5 inline size-3 align-[-0.125em]"
          />
          {securityNote}
        </p>

        <Button asChild size="sm" variant="link">
          <Link href={docsHref}>{secondaryAction}</Link>
        </Button>
      </div>
    </section>
  );
}
