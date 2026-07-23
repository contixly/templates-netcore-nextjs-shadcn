export const locales = ["en", "ru"] as const;

export type AppLocale = (typeof locales)[number];

export const DEFAULT_LOCALE: AppLocale = "en";
export const APP_TIME_ZONE = "UTC";

export function isAppLocale(value: string): value is AppLocale {
  return locales.includes(value as AppLocale);
}

export function resolveAppLocale(value?: string): AppLocale {
  return value && isAppLocale(value) ? value : DEFAULT_LOCALE;
}
