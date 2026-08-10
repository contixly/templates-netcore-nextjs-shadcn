import {
  IconAlertCircle,
  IconAlertTriangle,
  IconCircleCheck,
  IconFile,
  IconFolder,
  IconInfoCircle,
} from "@tabler/icons-react";
import type { MDXComponents } from "mdx/types";
import {
  Children,
  isValidElement,
  type ComponentProps,
  type ReactElement,
  type ReactNode,
} from "react";

import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/src/components/ui/tabs";
import { DocumentsCopyButton } from "@/src/features/documents/ui/documents-copy-button";
import {
  DocumentLinkCard,
  DocumentLinkGrid,
  DocumentLinkGroup,
} from "@/src/features/documents/ui/mdx/documents-link-grid";
import {
  createDocumentHeadingIdState,
  createUniqueDocumentHeadingId,
} from "@/src/features/documents/documents-heading-tools";
import { getDocumentsRegistry } from "@/src/features/documents/documents-registry";
import type { DocumentInfo } from "@/src/features/documents/documents-types";
import { cn } from "@/src/lib/utils";

type CalloutVariant = "default" | "info" | "success" | "warning" | "danger";
type TabProps = { children?: ReactNode; title: string; value: string };

const copyLabels = {
  en: { code: "Copy code", copied: "Copied", link: "Copy link" },
  ru: {
    code: "Копировать код",
    copied: "Скопировано",
    link: "Копировать ссылку",
  },
} as const;

const INLINE_LINK_CLASS =
  "font-medium text-foreground underline underline-offset-4 hover:text-primary";

function textFromNode(node: ReactNode): string {
  if (typeof node === "string" || typeof node === "number") return String(node);
  if (Array.isArray(node)) return node.map(textFromNode).join("");
  if (
    isValidElement<{
      children?: ReactNode;
      "data-footnote-ref"?: unknown;
    }>(node)
  ) {
    if ("data-footnote-ref" in node.props) return "";
    return textFromNode(node.props.children);
  }
  return "";
}

const CALLOUT_CLASS: Record<CalloutVariant, string> = {
  default: "border-border bg-background",
  info: "border-violet-500/35 bg-violet-500/[0.06]",
  success: "border-emerald-500/35 bg-emerald-500/[0.06]",
  warning: "border-yellow-500/40 bg-yellow-500/[0.08]",
  danger: "border-destructive/35 bg-destructive/[0.07]",
};

function Callout({
  children,
  title,
  variant = "default",
}: Readonly<{
  children?: ReactNode;
  title?: string;
  variant?: CalloutVariant;
}>) {
  const Icon =
    variant === "success"
      ? IconCircleCheck
      : variant === "info"
        ? IconInfoCircle
        : variant === "warning"
          ? IconAlertTriangle
          : IconAlertCircle;

  return (
    <Alert
      className={cn(
        "my-4 rounded-lg px-4 py-3.5 text-sm",
        CALLOUT_CLASS[variant],
        variant === "danger" && "text-destructive",
      )}
      data-callout-variant={variant}
      variant={variant === "danger" ? "destructive" : "default"}
    >
      <Icon aria-hidden="true" className="mt-0.5 size-4.5" />
      {title ? (
        <AlertTitle className="text-[13.5px] font-semibold">{title}</AlertTitle>
      ) : null}
      <AlertDescription className="text-[13.5px] leading-relaxed text-foreground [&_p]:mt-2 [&_p]:leading-relaxed">
        {children}
      </AlertDescription>
    </Alert>
  );
}

function Steps({ children }: Readonly<{ children?: ReactNode }>) {
  return (
    <ol className="my-6 flex list-none flex-col [counter-reset:step]">
      {children}
    </ol>
  );
}

function Step({
  children,
  title,
}: Readonly<{ children?: ReactNode; title: string }>) {
  return (
    <li className="group/step relative pb-6 pl-12 [counter-increment:step] before:absolute before:top-1 before:left-0 before:grid before:size-7 before:place-items-center before:rounded-full before:border before:bg-background before:font-mono before:text-[12px] before:font-semibold before:text-foreground before:content-[counter(step)] after:absolute after:top-9 after:bottom-0 after:left-[13.5px] after:w-px after:bg-border last:pb-0 last:after:hidden">
      <p className="text-sm font-semibold text-foreground">{title}</p>
      <div className="mt-1 text-sm leading-relaxed text-muted-foreground">
        {children}
      </div>
    </li>
  );
}

function Files({ children }: Readonly<{ children?: ReactNode }>) {
  return (
    <div className="my-4 rounded-lg border bg-background p-3 font-mono text-[13px]">
      {children}
    </div>
  );
}

function Folder({
  children,
  name,
}: Readonly<{ children?: ReactNode; name: string }>) {
  return (
    <div>
      <span className="flex items-center gap-2 py-1 font-medium">
        <IconFolder aria-hidden="true" className="size-4" />
        <span>{name}</span>/
      </span>
      {children ? (
        <div className="ml-3 flex flex-col gap-1 border-l border-dashed pl-4">
          {children}
        </div>
      ) : null}
    </div>
  );
}

function File({ name }: Readonly<{ name: string }>) {
  return (
    <span className="flex items-center gap-2 py-1">
      <IconFile aria-hidden="true" className="size-4" />
      {name}
    </span>
  );
}

function Tab({ children }: Readonly<TabProps>) {
  return <>{children}</>;
}

function DocumentsTabs({
  children,
  defaultValue,
}: Readonly<{ children?: ReactNode; defaultValue?: string }>) {
  const tabs = Children.toArray(children).filter(
    (child): child is ReactElement<TabProps> => isValidElement<TabProps>(child),
  );
  const initialValue = defaultValue ?? tabs[0]?.props.value;
  if (!initialValue) return null;

  return (
    <Tabs className="my-6 gap-4" defaultValue={initialValue}>
      <TabsList className="h-auto w-full justify-start overflow-x-auto rounded-md p-1 sm:w-fit">
        {tabs.map(({ props }) => (
          <TabsTrigger
            className="shrink-0"
            key={props.value}
            value={props.value}
          >
            {props.title}
          </TabsTrigger>
        ))}
      </TabsList>
      {tabs.map(({ props }) => (
        <TabsContent
          className="rounded-md border bg-muted/20 p-4"
          key={props.value}
          value={props.value}
        >
          <div className="grid gap-4 text-sm leading-6 text-foreground [&>*:first-child]:mt-0">
            {props.children}
          </div>
        </TabsContent>
      ))}
    </Tabs>
  );
}

function normalizedDocumentTarget(href: string): string | undefined {
  let parsed: URL;
  try {
    parsed = new URL(href, "https://documents.invalid");
  } catch {
    return undefined;
  }
  if (
    parsed.origin !== "https://documents.invalid" ||
    (parsed.pathname !== "/docs" && !parsed.pathname.startsWith("/docs/"))
  ) {
    return undefined;
  }
  const path = parsed.pathname.replace(/^\/docs\/?/u, "").replace(/\/+$/u, "");
  if (!path || path === "index") return "index";
  return path;
}

function isSafeDocumentHref(document: DocumentInfo, href: string): boolean {
  const target = normalizedDocumentTarget(href);
  if (target === undefined) return false;
  return getDocumentsRegistry(document.requestedLocale).visibleDocuments.some(
    ({ canonicalUrl }) => canonicalUrl === target,
  );
}

function safeImageSource(source: string | undefined): boolean {
  return Boolean(
    source && (source.startsWith("/") || /^https?:\/\//iu.test(source)),
  );
}

function normalizeSafeLinkHref(href: string | undefined): string | undefined {
  if (!href) return undefined;
  const normalized = href.trim();
  if (!normalized) return undefined;

  try {
    const protocol = new URL(normalized, "https://documents.invalid").protocol;
    return ["http:", "https:", "mailto:"].includes(protocol)
      ? normalized
      : undefined;
  } catch {
    return undefined;
  }
}

export function createDocumentMdxComponents(
  document: DocumentInfo,
): MDXComponents {
  const seenHeadings = createDocumentHeadingIdState();
  const labels = copyLabels[document.requestedLocale];

  function heading(level: 2 | 3) {
    return function DocumentHeading({
      children,
      className,
      ...props
    }: ComponentProps<"h2">) {
      const isGeneratedFootnoteLabel =
        level === 2 && props.id === "footnote-label";
      if (isGeneratedFootnoteLabel) {
        return (
          <h2 className={className} {...props}>
            {children}
          </h2>
        );
      }

      const text = textFromNode(children);
      const id = createUniqueDocumentHeadingId(text, seenHeadings);

      if (level === 3) {
        return (
          <h3
            {...props}
            className={cn(
              "mt-7 scroll-m-20 text-base font-semibold tracking-tight text-foreground",
              className,
            )}
            id={id}
          >
            {children}
          </h3>
        );
      }

      return (
        <h2
          {...props}
          className={cn(
            "group mt-10 flex scroll-m-20 items-center gap-3 border-b border-dashed border-muted-foreground/50 pb-0 text-xl font-semibold tracking-tight text-foreground first:mt-0",
            className,
          )}
          id={id}
        >
          <span className="min-w-0 flex-1">{children}</span>
          <DocumentsCopyButton
            href={`#${id}`}
            label={labels.link}
            successLabel={labels.copied}
          />
        </h2>
      );
    };
  }

  return {
    h1: ({ children, ...props }: ComponentProps<"h1">) => (
      <h1 className="hidden" {...props}>
        {children}
      </h1>
    ),
    h2: heading(2),
    h3: heading(3),
    p: ({ children, className, ...props }: ComponentProps<"p">) => (
      <p
        className={cn("mt-4 text-sm leading-6 text-foreground", className)}
        {...props}
      >
        {children}
      </p>
    ),
    ul: ({ children, className, ...props }: ComponentProps<"ul">) => {
      const taskList = className?.includes("contains-task-list");
      return (
        <ul
          className={cn(
            "mt-4 space-y-1.5 text-sm text-foreground marker:text-muted-foreground",
            taskList ? "ml-0 list-none" : "ml-6 list-disc",
            className,
          )}
          {...props}
        >
          {children}
        </ul>
      );
    },
    ol: ({ children, className, ...props }: ComponentProps<"ol">) => (
      <ol
        className={cn(
          "mt-4 ml-6 list-decimal space-y-1.5 text-sm text-foreground marker:text-muted-foreground",
          className,
        )}
        {...props}
      >
        {children}
      </ol>
    ),
    li: ({ children, className, ...props }: ComponentProps<"li">) => (
      <li
        className={cn(
          "leading-6",
          className?.includes("task-list-item") && "flex items-baseline gap-2",
          className,
        )}
        {...props}
      >
        {children}
      </li>
    ),
    input: ({ className, type, ...props }: ComponentProps<"input">) =>
      type === "checkbox" ? (
        <input
          className={cn(
            "relative size-3.5 shrink-0 translate-y-[3px] cursor-default appearance-none rounded-[3px] border border-border bg-background checked:border-foreground checked:bg-foreground checked:before:absolute checked:before:top-[1px] checked:before:left-[4px] checked:before:h-[7px] checked:before:w-[3px] checked:before:rotate-45 checked:before:border-r-[1.5px] checked:before:border-b-[1.5px] checked:before:border-background checked:before:content-[''] disabled:opacity-100",
            className,
          )}
          type="checkbox"
          {...props}
        />
      ) : (
        <input className={className} type={type} {...props} />
      ),
    del: ({ children, className, ...props }: ComponentProps<"del">) => (
      <del className={cn("text-muted-foreground", className)} {...props}>
        {children}
      </del>
    ),
    section: ({ children, className, ...props }: ComponentProps<"section">) => (
      <section
        className={cn(
          "data-[footnotes]:mt-12 data-[footnotes]:border-t data-[footnotes]:pt-6 data-[footnotes]:text-sm",
          className,
        )}
        {...props}
      >
        {children}
      </section>
    ),
    a: ({ children, className, href, ...props }: ComponentProps<"a">) => {
      const safeHref = normalizeSafeLinkHref(href);
      if (!safeHref) return <span>{children}</span>;
      const footnote =
        "data-footnote-ref" in props || "data-footnote-backref" in props;
      const linkClassName = footnote
        ? cn(
            "text-muted-foreground no-underline hover:text-foreground",
            "data-footnote-backref" in props && "ml-1",
            className,
          )
        : cn(INLINE_LINK_CLASS, className);

      if (/^(?:https?:)?\/\//iu.test(safeHref)) {
        return (
          <a
            {...props}
            className={linkClassName}
            href={safeHref}
            rel="noopener noreferrer"
            target="_blank"
          >
            {children}
          </a>
        );
      }
      if (
        safeHref.startsWith("/docs") &&
        !isSafeDocumentHref(document, safeHref)
      ) {
        return (
          <span
            aria-disabled="true"
            className={cn(
              INLINE_LINK_CLASS,
              "cursor-not-allowed text-muted-foreground hover:text-muted-foreground",
              className,
            )}
            data-document-link-state="unavailable"
          >
            {children}
          </span>
        );
      }
      return (
        <a {...props} className={linkClassName} href={safeHref}>
          {children}
        </a>
      );
    },
    img: ({
      alt = "",
      className,
      src,
      title,
      ...props
    }: ComponentProps<"img">) =>
      typeof src === "string" && safeImageSource(src) ? (
        <span className="my-6 block w-full">
          {/* MDX image dimensions are author-controlled, so native responsive
              sizing avoids inventing transport metadata. */}
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            {...props}
            alt={alt}
            className={cn(
              "block h-auto max-w-full rounded-lg border bg-background",
              className,
            )}
            loading="lazy"
            src={src}
            title={title}
          />
          {title ? (
            <span className="mt-2 block text-center text-xs text-muted-foreground">
              {title}
            </span>
          ) : null}
        </span>
      ) : null,
    blockquote: ({ className, ...props }: ComponentProps<"blockquote">) => (
      <blockquote
        className={cn(
          "mt-4 border-l-2 pl-4 text-sm leading-6 text-muted-foreground italic",
          className,
        )}
        {...props}
      />
    ),
    code: ({ className, ...props }: ComponentProps<"code">) => {
      const codeBlock = className?.startsWith("language-");
      return (
        <code
          className={cn(
            codeBlock
              ? "font-mono"
              : "rounded bg-muted px-1.5 py-0.5 font-mono text-sm",
            className,
          )}
          {...props}
        />
      );
    },
    pre: ({ children, className, ...props }: ComponentProps<"pre">) => (
      <div className="my-4 overflow-hidden rounded-lg border bg-muted/40">
        <div className="flex items-center justify-end border-b border-border bg-background/60 px-2 py-1">
          <DocumentsCopyButton
            label={labels.code}
            successLabel={labels.copied}
            value={textFromNode(children)}
          />
        </div>
        <div className="overflow-x-auto px-6 py-4">
          <pre
            className={cn("font-mono text-[12.5px] leading-relaxed", className)}
            {...props}
          >
            {children}
          </pre>
        </div>
      </div>
    ),
    table: ({ children, className, ...props }: ComponentProps<"table">) => (
      <div className="my-6 w-full overflow-x-auto">
        <table
          className={cn("w-full border-collapse text-sm", className)}
          {...props}
        >
          {children}
        </table>
      </div>
    ),
    th: ({ className, ...props }: ComponentProps<"th">) => (
      <th
        className={cn("border px-3 py-2 text-left font-medium", className)}
        {...props}
      />
    ),
    td: ({ className, ...props }: ComponentProps<"td">) => (
      <td
        className={cn("border px-3 py-2 text-muted-foreground", className)}
        {...props}
      />
    ),
    caption: ({ className, ...props }: ComponentProps<"caption">) => (
      <caption
        className={cn("mt-2 text-xs text-muted-foreground", className)}
        {...props}
      />
    ),
    Callout,
    Steps,
    Step,
    Files,
    Folder,
    File,
    Tabs: DocumentsTabs,
    Tab,
    DocumentLinkGrid,
    DocumentLinkGroup,
    DocumentLinkCard: ({
      href,
      ...props
    }: {
      children?: ReactNode;
      className?: string;
      href: string;
      title: string;
    }) => (
      <DocumentLinkCard
        {...props}
        available={isSafeDocumentHref(document, href)}
        href={href}
      />
    ),
  };
}
