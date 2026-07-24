"use client";

import { IconMoon, IconSun } from "@tabler/icons-react";
import { useTranslations } from "next-intl";
import { useTheme } from "next-themes";
import { useSyncExternalStore } from "react";

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
        aria-label={t("toggle")}
        disabled
        size="icon"
        title={t("toggle")}
        variant="outline"
      >
        <IconSun aria-hidden="true" />
      </Button>
    );
  }

  const nextTheme = resolvedTheme === "dark" ? "light" : "dark";
  const nextThemeLabel =
    nextTheme === "dark" ? t("switchToDark") : t("switchToLight");

  return (
    <Button
      aria-label={nextThemeLabel}
      onClick={() => setTheme(nextTheme)}
      size="icon"
      title={nextThemeLabel}
      variant="outline"
    >
      {nextTheme === "dark" ? (
        <IconMoon aria-hidden="true" />
      ) : (
        <IconSun aria-hidden="true" />
      )}
    </Button>
  );
}
