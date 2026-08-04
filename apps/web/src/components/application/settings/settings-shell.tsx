import { useId, type ReactNode } from "react";

import { PageHeader } from "@/src/components/application/page-header";
import { cn } from "@/src/lib/utils";

export type SettingsPageSectionMode = "wide" | "readable";
export type SettingsSectionVariant = "default" | "destructive";

export function SettingsPageShell({
  children,
}: Readonly<{ children: ReactNode }>) {
  return (
    <div
      className="mx-auto flex w-full max-w-6xl flex-1 flex-col md:flex-row"
      data-slot="settings-page-shell"
    >
      {children}
    </div>
  );
}

export function SettingsContentRail({
  children,
}: Readonly<{ children: ReactNode }>) {
  return (
    <div
      className="min-w-0 flex-1 px-4 py-8 md:px-6"
      data-slot="settings-content-rail"
    >
      {children}
    </div>
  );
}

export function SettingsPageSection({
  children,
  mode,
}: Readonly<{ children: ReactNode; mode: SettingsPageSectionMode }>) {
  return (
    <article
      className={cn(
        "mx-auto flex w-full flex-col gap-8",
        mode === "readable" ? "max-w-3xl" : "max-w-6xl",
      )}
      data-mode={mode}
      data-slot="settings-page-section"
    >
      {children}
    </article>
  );
}

export function SettingsPageIntro({
  description,
  title,
}: Readonly<{ description?: ReactNode; title: ReactNode }>) {
  return <PageHeader description={description} title={title} />;
}

export function SettingsSection({
  children,
  description,
  title,
  variant = "default",
}: Readonly<{
  children: ReactNode;
  description?: ReactNode;
  title: ReactNode;
  variant?: SettingsSectionVariant;
}>) {
  const headingId = useId();

  return (
    <section
      aria-labelledby={headingId}
      className={cn(
        "flex flex-col gap-4",
        variant === "destructive" &&
          "border border-destructive/40 bg-destructive/5 p-4",
      )}
      data-slot="settings-section"
      data-variant={variant}
      role="region"
    >
      <div className="flex flex-col gap-1">
        <h2
          className={cn(
            "text-sm font-semibold",
            variant === "destructive" && "text-destructive",
          )}
          id={headingId}
        >
          {title}
        </h2>
        {description ? (
          <p className="text-sm text-muted-foreground">{description}</p>
        ) : null}
      </div>
      {children}
    </section>
  );
}
