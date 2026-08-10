"use client";

import { IconMoon, IconSun } from "@tabler/icons-react";
import { useTranslations } from "next-intl";
import { useTheme } from "next-themes";
import { useSyncExternalStore } from "react";

import { INTERACTION_READY_ATTRIBUTE } from "@/src/features/application/ui/interaction-readiness";
import { Button } from "@/src/components/ui/button";

export function ThemeSwitcher() {
  const t = useTranslations("common.theme");
  const { resolvedTheme, setTheme } = useTheme();
  const mounted = useSyncExternalStore(
    () => () => {},
    () => true,
    () => false,
  );

  if (!mounted || !resolvedTheme) {
    return (
      <Button
        {...{ [INTERACTION_READY_ATTRIBUTE]: false }}
        aria-label={t("toggle")}
        disabled
        size="icon"
        title={t("toggle")}
        variant="outline"
      >
        <IconSun
          aria-hidden="true"
          className="size-[1.2rem] scale-100 rotate-0 transition-all dark:scale-0 dark:-rotate-90"
        />
        <IconMoon
          aria-hidden="true"
          className="absolute size-[1.2rem] scale-0 rotate-90 transition-all dark:scale-100 dark:rotate-0"
        />
      </Button>
    );
  }

  const nextTheme = resolvedTheme === "dark" ? "light" : "dark";
  const nextThemeLabel =
    nextTheme === "dark" ? t("switchToDark") : t("switchToLight");

  return (
    <Button
      {...{ [INTERACTION_READY_ATTRIBUTE]: true }}
      aria-label={nextThemeLabel}
      onClick={() => setTheme(nextTheme)}
      size="icon"
      title={nextThemeLabel}
      variant="outline"
    >
      <IconSun
        aria-hidden="true"
        className="size-[1.2rem] scale-100 rotate-0 transition-all dark:scale-0 dark:-rotate-90"
      />
      <IconMoon
        aria-hidden="true"
        className="absolute size-[1.2rem] scale-0 rotate-90 transition-all dark:scale-100 dark:rotate-0"
      />
    </Button>
  );
}
