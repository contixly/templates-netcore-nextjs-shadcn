import { useId, type PropsWithChildren, type ReactNode } from "react";

import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import { cn } from "@/src/lib/utils";

export type SettingsPageSectionMode = "wide" | "readable";
export type SettingsSectionVariant = "default" | "destructive";

export function SettingsPageShell({
  children,
  className,
}: PropsWithChildren<{ className?: string }>) {
  return (
    <div
      className={cn(
        "flex flex-1 flex-col gap-0 md:flex-row xl:gap-8",
        className,
      )}
      data-slot="settings-page-shell"
    >
      {children}
    </div>
  );
}

export function SettingsContentRail({
  children,
  className,
}: PropsWithChildren<{ className?: string }>) {
  return (
    <div
      className={cn("min-w-0 flex-1 px-2 py-4 md:px-4 xl:px-6", className)}
      data-slot="settings-content-rail"
    >
      <div
        className="mx-auto flex w-full max-w-6xl flex-col gap-6"
        data-slot="settings-page-rail"
      >
        {children}
      </div>
    </div>
  );
}

export function SettingsPageSection({
  children,
  className,
  mode = "wide",
}: PropsWithChildren<{
  className?: string;
  mode?: SettingsPageSectionMode;
}>) {
  return (
    <section
      className={cn(
        "grid w-full gap-4",
        mode === "readable" && "max-w-3xl",
        className,
      )}
      data-mode={mode}
      data-slot="settings-page-section"
    >
      {children}
    </section>
  );
}

export function SettingsPageIntro({
  action,
  className,
  description,
  title,
}: Readonly<{
  action?: ReactNode;
  className?: string;
  description?: ReactNode;
  title: ReactNode;
}>) {
  return (
    <header
      className={cn(
        "hidden flex-col gap-3 border-b pb-5 sm:flex-row sm:items-start sm:justify-between md:flex",
        className,
      )}
      data-slot="settings-page-intro"
    >
      <div className="flex min-w-0 flex-col gap-1.5">
        <h1 className="text-2xl font-semibold tracking-normal text-foreground">
          {title}
        </h1>
        {description ? (
          <p className="max-w-2xl text-sm text-muted-foreground">
            {description}
          </p>
        ) : null}
      </div>
      {action ? <div className="shrink-0">{action}</div> : null}
    </header>
  );
}

export function SettingsSection({
  action,
  children,
  className,
  contentClassName,
  description,
  footer,
  headingLevel = 2,
  title,
  variant = "default",
}: PropsWithChildren<{
  action?: ReactNode;
  className?: string;
  contentClassName?: string;
  description?: ReactNode;
  footer?: ReactNode;
  headingLevel?: 2 | 3;
  title: ReactNode;
  variant?: SettingsSectionVariant;
}>) {
  const titleId = useId();
  const isDestructive = variant === "destructive";
  const Heading = headingLevel === 3 ? "h3" : "h2";

  return (
    <Card
      aria-labelledby={titleId}
      className={cn(
        "gap-0 py-0",
        isDestructive && "ring-destructive/40",
        className,
      )}
      data-slot="settings-section"
      data-variant={variant}
      role="region"
    >
      <CardHeader className="border-b px-5 py-4 sm:px-6">
        <CardTitle>
          <Heading
            className={cn(
              "text-sm font-medium",
              isDestructive && "text-destructive",
            )}
            id={titleId}
          >
            {title}
          </Heading>
        </CardTitle>
        {description ? (
          <CardDescription
            className={cn(isDestructive && "text-destructive/80")}
          >
            {description}
          </CardDescription>
        ) : null}
        {action ? (
          <CardAction>
            <div
              className="flex items-center gap-2"
              data-slot="settings-section-action"
            >
              {action}
            </div>
          </CardAction>
        ) : null}
      </CardHeader>
      <CardContent className={cn("px-5 py-5 sm:px-6", contentClassName)}>
        <div className="min-w-0" data-slot="settings-section-content">
          {children}
        </div>
      </CardContent>
      {footer ? (
        <CardFooter className="px-5 py-4 sm:px-6">
          <div className="min-w-0" data-slot="settings-section-footer">
            {footer}
          </div>
        </CardFooter>
      ) : null}
    </Card>
  );
}
