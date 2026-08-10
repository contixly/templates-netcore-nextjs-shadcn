import { IconArrowRight, type TablerIcon } from "@tabler/icons-react";
import type { Route } from "next";
import Link from "next/link";

import { Button } from "@/src/components/ui/button";
import {
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";

export type LandingFeature = Readonly<{
  description: string;
  icon: TablerIcon;
  title: string;
}>;

type LandingFeaturesProps = Readonly<{
  description: string;
  features: readonly LandingFeature[];
  loginHref: Route;
  title: string;
  valueAction: string;
  valueDescription: string;
  valueEyebrow: string;
  valueTitle: string;
}>;

export function LandingFeatures({
  description,
  features,
  loginHref,
  title,
  valueAction,
  valueDescription,
  valueEyebrow,
  valueTitle,
}: LandingFeaturesProps) {
  return (
    <>
      <section
        aria-labelledby="landing-features-title"
        className="flex flex-col items-center gap-10 px-4 py-16 md:px-6 md:py-24"
      >
        <div className="flex w-full max-w-2xl min-w-0 flex-col items-center gap-3 text-center">
          <h2
            className="w-full text-xl font-bold tracking-tight text-pretty md:text-2xl"
            id="landing-features-title"
          >
            {title}
          </h2>
          <p className="w-full text-sm text-muted-foreground">{description}</p>
        </div>

        <div className="grid w-full max-w-5xl gap-4 md:grid-cols-2" role="list">
          {features.map((feature) => (
            <Card key={feature.title} role="listitem">
              <CardHeader>
                <feature.icon
                  aria-hidden="true"
                  className="mb-2 size-5 text-foreground"
                />
                <CardTitle>{feature.title}</CardTitle>
                <CardDescription>{feature.description}</CardDescription>
              </CardHeader>
            </Card>
          ))}
        </div>
      </section>

      <section className="flex flex-col items-center gap-6 border-y bg-muted/35 px-4 py-16 text-center md:px-6 md:py-24">
        <p className="text-xs font-semibold tracking-[0.2em] text-muted-foreground uppercase">
          {valueEyebrow}
        </p>
        <h2 className="w-full text-xl font-bold tracking-tight text-pretty md:text-2xl">
          {valueTitle}
        </h2>
        <p className="w-full max-w-md text-sm text-muted-foreground">
          {valueDescription}
        </p>
        <Button asChild size="lg">
          <Link href={loginHref}>
            {valueAction}
            <IconArrowRight aria-hidden="true" />
          </Link>
        </Button>
      </section>
    </>
  );
}
