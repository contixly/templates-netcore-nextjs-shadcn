"use client";

import { IconMoon, IconSun } from "@tabler/icons-react";
import { useTranslations } from "next-intl";
import { useTheme } from "next-themes";
import { useSyncExternalStore } from "react";

import { INTERACTION_READY_ATTRIBUTE } from "@/src/components/application/interaction-readiness";
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
        <IconSun aria-hidden="true" />
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
      {nextTheme === "dark" ? (
        <IconMoon aria-hidden="true" />
      ) : (
        <IconSun aria-hidden="true" />
      )}
    </Button>
  );
}
