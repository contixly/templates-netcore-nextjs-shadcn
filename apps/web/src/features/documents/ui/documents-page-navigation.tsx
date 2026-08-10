import { IconArrowLeft, IconArrowRight } from "@tabler/icons-react";
import type { Route } from "next";
import Link from "next/link";
import { useTranslations } from "next-intl";

import { ButtonGroup } from "@/src/components/ui/button-group";
import { ButtonWithTooltip } from "@/src/components/ui/custom/button-with-tooltip";
import {
  Item,
  ItemActions,
  ItemContent,
  ItemDescription,
  ItemTitle,
} from "@/src/components/ui/item";
import type { DocumentPageNavigation } from "@/src/features/documents/documents-types";
import { cn } from "@/src/lib/utils";

export function DocumentsPageNavigation({
  navigation,
  placement = "bottom",
}: Readonly<{
  navigation?: DocumentPageNavigation;
  placement?: "top" | "bottom";
}>) {
  const t = useTranslations("documents.page");

  if (!navigation?.previous && !navigation?.next) return null;

  if (placement === "top") {
    return (
      <nav
        aria-label={t("navigation")}
        className="fixed inset-x-0 bottom-0 isolate z-50 flex items-center gap-2 border-t border-border/50 bg-background/80 px-6 py-4 backdrop-blur-sm sm:absolute sm:inset-x-auto sm:top-7 sm:right-6 sm:bottom-auto sm:z-10 sm:border-0 sm:bg-transparent sm:px-0 sm:py-0 sm:backdrop-blur-none lg:right-10 xl:right-[calc(18rem+6rem)]"
      >
        <ButtonGroup className="items-center">
          {navigation.previous ? (
            <ButtonWithTooltip
              asChild
              tooltipContent={navigation.previous.title}
              variant="outline"
            >
              <Link
                aria-label={`${t("previous")}: ${navigation.previous.title}`}
                href={navigation.previous.href as Route}
              >
                <IconArrowLeft />
              </Link>
            </ButtonWithTooltip>
          ) : (
            <ButtonWithTooltip
              aria-label={t("previous")}
              disabled
              tooltipContent={undefined}
              variant="outline"
            >
              <IconArrowLeft />
            </ButtonWithTooltip>
          )}
          {navigation.next ? (
            <ButtonWithTooltip
              asChild
              tooltipContent={navigation.next.title}
              variant="outline"
            >
              <Link
                aria-label={`${t("next")}: ${navigation.next.title}`}
                href={navigation.next.href as Route}
              >
                <IconArrowRight />
              </Link>
            </ButtonWithTooltip>
          ) : (
            <ButtonWithTooltip
              aria-label={t("next")}
              disabled
              tooltipContent={undefined}
              variant="outline"
            >
              <IconArrowRight />
            </ButtonWithTooltip>
          )}
        </ButtonGroup>
      </nav>
    );
  }

  const items = [navigation.previous, navigation.next] as const;

  return (
    <nav
      aria-label={t("navigation")}
      className="mt-14 grid grid-cols-1 gap-4 pb-6 lg:grid-cols-2"
    >
      {items.map((item, index) => (
        <div key={`${index}_${item?.href ?? "empty"}`}>
          {item ? (
            <Item asChild className="h-full" variant="outline">
              <Link
                aria-label={`${index === 0 ? t("previous") : t("next")}: ${item.title}`}
                href={item.href as Route}
              >
                {index === 0 ? (
                  <ItemActions>
                    <IconArrowLeft className="size-4" />
                  </ItemActions>
                ) : null}
                <ItemContent className={cn(index === 1 && "text-right")}>
                  <ItemTitle className={cn(index === 1 && "self-end")}>
                    {item.title}
                  </ItemTitle>
                  <ItemDescription>{item.description}</ItemDescription>
                </ItemContent>
                {index === 1 ? (
                  <ItemActions>
                    <IconArrowRight className="size-4" />
                  </ItemActions>
                ) : null}
              </Link>
            </Item>
          ) : null}
        </div>
      ))}
    </nav>
  );
}
