"use client";

import { IconBook2, IconChevronRight } from "@tabler/icons-react";
import type { Route } from "next";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useState } from "react";

import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/src/components/ui/collapsible";
import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuSub,
  SidebarMenuSubButton,
  SidebarMenuSubItem,
  SidebarRail,
} from "@/src/components/ui/sidebar";
import { documentsRoutes } from "@/src/features/documents/documents-routes";
import type {
  DocumentStatus,
  DocumentsSidebarGroup,
} from "@/src/features/documents/documents-types";
import { useMobileSidebarClose } from "@/src/hooks/use-mobile-sidebar-close";
import { cn } from "@/src/lib/utils";

type SidebarStatusStripe = Exclude<DocumentStatus, "published">;

const STATUS_STRIPE_CLASS: Record<SidebarStatusStripe, string> = {
  draft: "bg-amber-500/80 dark:bg-amber-300/80",
  review: "bg-sky-500/80 dark:bg-sky-300/80",
  archived: "bg-muted-foreground/45 dark:bg-muted-foreground/60",
};

function StatusStripes({
  className,
  statuses,
}: Readonly<{ className?: string; statuses: readonly DocumentStatus[] }>) {
  const stripes = [...new Set(statuses)].filter(
    (status): status is SidebarStatusStripe => status !== "published",
  );

  if (stripes.length === 0) return null;

  return (
    <span
      aria-hidden="true"
      className={cn("flex h-5 shrink-0 items-stretch gap-0.5", className)}
      data-status-stripes={stripes.join(" ")}
    >
      {stripes.map((stripe) => (
        <span
          className={cn("w-1 rounded-full", STATUS_STRIPE_CLASS[stripe])}
          data-status-stripe={stripe}
          key={stripe}
        />
      ))}
    </span>
  );
}

function DocumentsSidebarParent({
  currentHref,
  items,
  label,
  onNavigate,
}: Readonly<{
  currentHref: string;
  items: DocumentsSidebarGroup["parents"][number]["items"];
  label: string;
  onNavigate?: () => void;
}>) {
  const hasActiveChild = items.some((item) => item.href === currentHref);
  const [open, setOpen] = useState(hasActiveChild);
  const closeMobileSidebar = useMobileSidebarClose();

  return (
    <Collapsible
      className="group/collapsible"
      onOpenChange={setOpen}
      open={open || hasActiveChild}
    >
      <SidebarMenuItem>
        <CollapsibleTrigger asChild>
          <SidebarMenuButton className="h-auto min-h-8 items-start px-2 py-1.5 text-sm leading-5 font-medium text-sidebar-foreground [&>span]:min-w-0 [&>span]:flex-1 [&>span]:break-words [&>span]:whitespace-normal">
            <span>{label}</span>
            <StatusStripes
              className="mt-0.5"
              statuses={items.map(({ status }) => status)}
            />
            <IconChevronRight className="mt-0.5 shrink-0 transition-transform duration-200 group-data-[state=open]/collapsible:rotate-90" />
          </SidebarMenuButton>
        </CollapsibleTrigger>
        <CollapsibleContent>
          <SidebarMenuSub className="mx-3 border-l border-dashed px-0 py-1.5">
            {items.map((item) => {
              const isCurrent = item.href === currentHref;

              return (
                <SidebarMenuSubItem key={item.canonicalUrl}>
                  <SidebarMenuSubButton
                    asChild
                    className="relative ml-1.5 h-auto min-h-7 items-start rounded-none py-1.5 pr-2 pl-4 text-[13px] leading-5 break-words whitespace-normal text-sidebar-foreground/65 data-[active=true]:bg-sidebar-primary/10 data-[active=true]:font-medium data-[active=true]:text-sidebar-accent-foreground"
                    data-status-tone={item.status}
                    isActive={isCurrent}
                  >
                    <Link
                      aria-current={isCurrent ? "page" : undefined}
                      href={item.href as Route}
                      onClick={() => {
                        closeMobileSidebar();
                        onNavigate?.();
                      }}
                    >
                      <span className="min-w-0 flex-1 break-words whitespace-normal">
                        {item.label}
                      </span>
                      <StatusStripes className="h-4" statuses={[item.status]} />
                    </Link>
                  </SidebarMenuSubButton>
                </SidebarMenuSubItem>
              );
            })}
          </SidebarMenuSub>
        </CollapsibleContent>
      </SidebarMenuItem>
    </Collapsible>
  );
}

export function DocumentsSidebar({
  currentHref,
  navigation,
  onNavigate,
}: Readonly<{
  currentHref: string;
  navigation: DocumentsSidebarGroup[];
  onNavigate?: () => void;
}>) {
  const t = useTranslations("documents");

  return (
    <Sidebar
      mobileDescription={t("navigation.label")}
      mobileTitle={t("sidebar.title")}
    >
      <SidebarHeader>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton asChild size="lg">
              <Link href={documentsRoutes.root} onClick={onNavigate}>
                <span className="flex aspect-square size-8 items-center justify-center rounded-md bg-sidebar-primary text-sidebar-primary-foreground">
                  <IconBook2 className="size-4" />
                </span>
                <span className="flex flex-col gap-0.5 leading-none">
                  <span className="text-sm font-medium">
                    {t("sidebar.title")}
                  </span>
                  <span className="text-xs text-sidebar-foreground/60">
                    v1.0.0
                  </span>
                </span>
              </Link>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>
      <SidebarContent className="gap-2 py-2">
        <nav aria-label={t("navigation.label")}>
          {navigation.map((group) => (
            <SidebarGroup className="px-2 py-1.5" key={group.label}>
              <SidebarMenu className="gap-1.5">
                <SidebarGroupLabel className="h-auto min-h-6 border-b border-dashed px-2 pt-0.5 pb-1.5 text-xs leading-4 font-semibold text-sidebar-foreground/55 capitalize">
                  {group.label}
                </SidebarGroupLabel>
                {group.parents.map((parent) => (
                  <DocumentsSidebarParent
                    currentHref={currentHref}
                    items={parent.items}
                    key={parent.label}
                    label={parent.label}
                    onNavigate={onNavigate}
                  />
                ))}
              </SidebarMenu>
            </SidebarGroup>
          ))}
        </nav>
      </SidebarContent>
      <SidebarRail />
    </Sidebar>
  );
}
