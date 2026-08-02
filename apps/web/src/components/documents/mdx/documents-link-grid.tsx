import type { ReactNode } from "react";

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";

export function DocumentLinkGrid({
  children,
}: Readonly<{ children?: ReactNode }>) {
  return <div className="grid gap-6 lg:grid-cols-2">{children}</div>;
}

export function DocumentLinkGroup({
  children,
  description,
  title,
}: Readonly<{ children?: ReactNode; description?: string; title: string }>) {
  return (
    <section className="flex flex-col gap-3">
      <header className="flex flex-col gap-1">
        <h3 className="text-base font-semibold">{title}</h3>
        {description ? (
          <p className="text-xs/relaxed text-muted-foreground">{description}</p>
        ) : null}
      </header>
      <div className="flex flex-col gap-3">{children}</div>
    </section>
  );
}

export function DocumentLinkCard({
  available,
  children,
  href,
  title,
}: Readonly<{
  available: boolean;
  children?: ReactNode;
  href: string;
  title: string;
}>) {
  const card = (
    <Card className="h-full transition-colors hover:bg-muted/40" size="sm">
      <CardHeader>
        <CardTitle>{title}</CardTitle>
      </CardHeader>
      <CardContent>
        <CardDescription>{children}</CardDescription>
      </CardContent>
    </Card>
  );

  return available ? (
    <a className="block" href={href}>
      {card}
    </a>
  ) : (
    <div aria-disabled="true" data-document-link-state="unavailable">
      {card}
    </div>
  );
}
