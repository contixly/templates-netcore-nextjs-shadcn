import {
  IconAlertCircle,
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

import { DocumentsCopyButton } from "@/src/components/documents/documents-copy-button";
import {
  DocumentLinkCard,
  DocumentLinkGrid,
  DocumentLinkGroup,
} from "@/src/components/documents/mdx/documents-link-grid";
import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
} from "@/src/components/ui/table";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/src/components/ui/tabs";
import { createUniqueDocumentHeadingId } from "@/src/features/documents/documents-heading-tools";
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

function textFromNode(node: ReactNode): string {
  if (typeof node === "string" || typeof node === "number") return String(node);
  if (Array.isArray(node)) return node.map(textFromNode).join("");
  if (isValidElement<{ children?: ReactNode }>(node))
    return textFromNode(node.props.children);
  return "";
}

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
        : IconAlertCircle;
  return (
    <Alert
      className={cn(
        "my-6",
        variant === "danger" && "border-destructive text-destructive",
        variant === "warning" && "border-foreground/30",
      )}
      data-callout-variant={variant}
      variant={variant === "danger" ? "destructive" : "default"}
    >
      <Icon aria-hidden="true" />
      {title ? <AlertTitle>{title}</AlertTitle> : null}
      <AlertDescription>{children}</AlertDescription>
    </Alert>
  );
}

function Steps({ children }: Readonly<{ children?: ReactNode }>) {
  return (
    <ol className="my-6 flex list-none flex-col gap-5 border-l pl-6 [counter-reset:step]">
      {children}
    </ol>
  );
}

function Step({
  children,
  title,
}: Readonly<{ children?: ReactNode; title: string }>) {
  return (
    <li className="relative flex flex-col gap-1 [counter-increment:step] before:absolute before:-left-9 before:flex before:size-6 before:items-center before:justify-center before:border before:bg-background before:text-xs before:content-[counter(step)]">
      <p className="font-semibold">{title}</p>
      <div>{children}</div>
    </li>
  );
}

function Files({ children }: Readonly<{ children?: ReactNode }>) {
  return <div className="my-6 border p-4 font-mono text-xs">{children}</div>;
}

function Folder({
  children,
  name,
}: Readonly<{ children?: ReactNode; name: string }>) {
  return (
    <div className="flex flex-col gap-1">
      <span className="flex items-center gap-2 font-medium">
        <IconFolder aria-hidden="true" />
        {name}
      </span>
      <div className="ml-2 flex flex-col gap-1 border-l pl-4">{children}</div>
    </div>
  );
}

function File({ name }: Readonly<{ name: string }>) {
  return (
    <span className="flex items-center gap-2">
      <IconFile aria-hidden="true" />
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
    <Tabs className="my-6" defaultValue={initialValue}>
      <TabsList>
        {tabs.map(({ props }) => (
          <TabsTrigger key={props.value} value={props.value}>
            {props.title}
          </TabsTrigger>
        ))}
      </TabsList>
      <div className="border p-4">
        {tabs.map(({ props }) => (
          <TabsContent key={props.value} value={props.value}>
            {props.children}
          </TabsContent>
        ))}
      </div>
    </Tabs>
  );
}

function normalizedDocumentTarget(href: string): string | undefined {
  if (!href.startsWith("/docs")) return undefined;
  const parsed = new URL(href, "https://documents.invalid");
  if (parsed.origin !== "https://documents.invalid") return undefined;
  let path = parsed.pathname.replace(/^\/docs\/?/u, "").replace(/\/$/u, "");
  if (!path || path === "index") return "index";
  if (path.endsWith("/index")) path = path.slice(0, -6);
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
    source && (source.startsWith("/") || /^https?:\/\//u.test(source)),
  );
}

export function createDocumentMdxComponents(
  document: DocumentInfo,
): MDXComponents {
  const seenHeadings = new Map<string, number>();
  const labels = copyLabels[document.requestedLocale];

  function heading(level: 2 | 3) {
    return function DocumentHeading({
      children,
      ...props
    }: ComponentProps<"h2">) {
      const text = textFromNode(children);
      const id = createUniqueDocumentHeadingId(text, seenHeadings);
      const Heading = `h${level}` as const;
      return (
        <Heading
          {...props}
          className={
            level === 2
              ? "group mt-10 flex scroll-mt-6 items-center gap-2 text-xl font-semibold"
              : "group mt-8 flex scroll-mt-6 items-center gap-2 text-base font-semibold"
          }
          id={id}
        >
          <span>{children}</span>
          <DocumentsCopyButton
            href={`#${id}`}
            label={labels.link}
            successLabel={labels.copied}
          />
        </Heading>
      );
    };
  }

  return {
    h1: () => null,
    h2: heading(2),
    h3: heading(3),
    a: ({ children, href, ...props }: ComponentProps<"a">) => {
      if (!href || /^javascript:/iu.test(href)) return <span>{children}</span>;
      if (/^(?:https?:)?\/\//iu.test(href)) {
        return (
          <a {...props} href={href} rel="noopener noreferrer" target="_blank">
            {children}
          </a>
        );
      }
      if (href.startsWith("/docs") && !isSafeDocumentHref(document, href)) {
        return (
          <span aria-disabled="true" data-document-link-state="unavailable">
            {children}
          </span>
        );
      }
      if (/^[a-z][a-z\d+.-]*:/iu.test(href) && !/^mailto:/iu.test(href)) {
        return <span>{children}</span>;
      }
      return (
        <a {...props} href={href}>
          {children}
        </a>
      );
    },
    img: ({ alt = "", src, ...props }: ComponentProps<"img">) =>
      typeof src === "string" && safeImageSource(src) ? (
        // MDX images have author-defined local or remote dimensions, so a native
        // responsive image is safer than inventing Next Image dimensions.
        // eslint-disable-next-line @next/next/no-img-element
        <img
          {...props}
          alt={alt}
          className="h-auto max-w-full border"
          loading="lazy"
          src={src}
        />
      ) : null,
    blockquote: (props: ComponentProps<"blockquote">) => (
      <blockquote
        className="border-l-2 pl-4 text-muted-foreground"
        {...props}
      />
    ),
    code: (props: ComponentProps<"code">) => (
      <code className="bg-muted px-1 py-0.5 font-mono text-xs" {...props} />
    ),
    pre: ({ children, ...props }: ComponentProps<"pre">) => (
      <div className="relative my-6">
        <pre
          className="overflow-x-auto bg-muted p-4 font-mono text-xs"
          {...props}
        >
          {children}
        </pre>
        <div className="absolute top-2 right-2">
          <DocumentsCopyButton
            label={labels.code}
            successLabel={labels.copied}
            value={textFromNode(children)}
          />
        </div>
      </div>
    ),
    table: Table,
    thead: TableHeader,
    tbody: TableBody,
    tfoot: TableFooter,
    tr: TableRow,
    th: TableHead,
    td: TableCell,
    caption: TableCaption,
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
