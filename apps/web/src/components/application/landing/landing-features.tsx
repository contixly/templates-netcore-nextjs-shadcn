import type { TablerIcon } from "@tabler/icons-react";

export type LandingFeature = Readonly<{
  description: string;
  icon: TablerIcon;
  title: string;
}>;

type LandingFeaturesProps = Readonly<{
  description: string;
  features: readonly LandingFeature[];
  title: string;
  valueDescription: string;
  valueEyebrow: string;
  valueTitle: string;
}>;

export function LandingFeatures({
  description,
  features,
  title,
  valueDescription,
  valueEyebrow,
  valueTitle,
}: LandingFeaturesProps) {
  return (
    <>
      <section
        aria-labelledby="landing-features-title"
        className="px-4 py-16 sm:px-6 md:py-24 lg:px-8"
      >
        <div className="mx-auto max-w-5xl">
          <div className="grid gap-4 border-b pb-10 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)] md:gap-12">
            <h2
              className="text-2xl font-semibold tracking-tight text-balance sm:text-3xl"
              id="landing-features-title"
            >
              {title}
            </h2>
            <p className="max-w-xl text-sm/7 text-muted-foreground sm:text-base/7">
              {description}
            </p>
          </div>
          <div className="grid gap-px bg-border sm:grid-cols-2" role="list">
            {features.map((feature) => (
              <article
                className="group bg-background p-6 transition-colors hover:bg-muted/40 sm:p-8"
                key={feature.title}
                role="listitem"
              >
                <feature.icon
                  aria-hidden="true"
                  className="mb-8 size-5 text-muted-foreground transition-colors group-hover:text-foreground"
                />
                <h3 className="text-base font-semibold">{feature.title}</h3>
                <p className="mt-2 max-w-md text-sm/6 text-muted-foreground">
                  {feature.description}
                </p>
              </article>
            ))}
          </div>
        </div>
      </section>

      <section className="border-y bg-muted/35 px-4 py-16 sm:px-6 md:py-24 lg:px-8">
        <div className="mx-auto grid max-w-5xl gap-6 md:grid-cols-[minmax(0,0.65fr)_minmax(0,1.35fr)] md:gap-16">
          <p className="text-xs font-semibold tracking-[0.2em] text-muted-foreground uppercase">
            {valueEyebrow}
          </p>
          <div>
            <h2 className="text-2xl font-semibold tracking-tight text-balance sm:text-3xl">
              {valueTitle}
            </h2>
            <p className="mt-4 max-w-2xl text-sm/7 text-muted-foreground sm:text-base/7">
              {valueDescription}
            </p>
          </div>
        </div>
      </section>
    </>
  );
}
