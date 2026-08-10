import { IconChevronRight } from "@tabler/icons-react";
import type { ReactNode } from "react";

import { cn } from "@/src/lib/utils";

const DOCUMENT_LINK_CARD_LAYOUT_CLASS =
  "flex min-w-0 items-start justify-between gap-3 rounded-md border px-4 py-3 text-sm no-underline shadow-xs";

function DocumentLinkCardContent({
  available,
  children,
  title,
}: Readonly<{
  available: boolean;
  children?: ReactNode;
  title: string;
}>) {
  return (
    <>
      <span className="min-w-0">
        <span
          className={cn(
            "min-w-0 leading-5 font-semibold",
            available ? "text-foreground" : "text-foreground/70",
          )}
        >
          {title}
        </span>
        {children ? (
          <span
            className={cn(
              "mt-1.5 block text-[13px] leading-5",
              available ? "text-foreground/70" : "text-muted-foreground",
            )}
          >
            {children}
          </span>
        ) : null}
      </span>
      <IconChevronRight
        aria-hidden="true"
        className={cn(
          "mt-0.5 size-4 shrink-0 transition-transform",
          available
            ? "text-foreground/55 group-hover:translate-x-0.5 group-hover:text-foreground"
            : "text-muted-foreground/70",
        )}
      />
    </>
  );
}

export function DocumentLinkGrid({
  children,
  className,
}: Readonly<{ children?: ReactNode; className?: string }>) {
  return (
    <div
      className={cn(
        "my-8 grid grid-cols-1 gap-x-6 gap-y-6 md:grid-cols-2 [&>*]:min-w-0",
        className,
      )}
    >
      {children}
    </div>
  );
}

export function DocumentLinkGroup({
  children,
  className,
  description,
  title,
}: Readonly<{
  children?: ReactNode;
  className?: string;
  description?: string;
  title: string;
}>) {
  return (
    <section
      className={cn(
        "rounded-lg border border-border/80 bg-muted/45 p-5 shadow-xs",
        className,
      )}
    >
      <header className="flex flex-col gap-1.5">
        <h3 className="m-0 text-xl leading-tight font-semibold text-foreground">
          {title}
        </h3>
        {description ? (
          <p className="m-0 text-sm leading-6 text-foreground/70">
            {description}
          </p>
        ) : null}
      </header>
      <div className="mt-4 grid gap-2.5">{children}</div>
    </section>
  );
}

export function DocumentLinkCard({
  available,
  children,
  className,
  href,
  title,
}: Readonly<{
  available: boolean;
  children?: ReactNode;
  className?: string;
  href: string;
  title: string;
}>) {
  if (!available) {
    return (
      <span
        aria-disabled="true"
        className={cn(
          DOCUMENT_LINK_CARD_LAYOUT_CLASS,
          "cursor-not-allowed border-border/70 bg-muted/35 text-muted-foreground opacity-75",
          className,
        )}
        data-document-link-state="unavailable"
      >
        <DocumentLinkCardContent available={false} title={title}>
          {children}
        </DocumentLinkCardContent>
      </span>
    );
  }

  return (
    <a
      className={cn(
        DOCUMENT_LINK_CARD_LAYOUT_CLASS,
        "group border-border/80 bg-background transition-[border-color,background-color,box-shadow] hover:border-ring/45 hover:bg-card hover:shadow-sm focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/35 focus-visible:outline-none",
        className,
      )}
      href={href}
    >
      <DocumentLinkCardContent available title={title}>
        {children}
      </DocumentLinkCardContent>
    </a>
  );
}
