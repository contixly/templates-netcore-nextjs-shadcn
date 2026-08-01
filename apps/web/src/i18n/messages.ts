import accountEn from "@/src/messages/account.en.json";
import accountRu from "@/src/messages/account.ru.json";
import authEn from "@/src/messages/auth.en.json";
import authRu from "@/src/messages/auth.ru.json";
import commonEn from "@/src/messages/common.en.json";
import commonRu from "@/src/messages/common.ru.json";
import organizationsEn from "@/src/messages/organizations.en.json";
import organizationsRu from "@/src/messages/organizations.ru.json";
import systemEn from "@/src/messages/system.en.json";
import systemRu from "@/src/messages/system.ru.json";
import {
  APP_TIME_ZONE,
  resolveAppLocale,
  type AppLocale,
} from "@/src/i18n/config";

const englishMessages = {
  account: accountEn,
  auth: authEn,
  common: commonEn,
  organizations: organizationsEn,
  system: systemEn,
};

export type I18nMessages = typeof englishMessages;

const messagesByLocale = {
  en: englishMessages,
  ru: {
    account: accountRu,
    auth: authRu,
    common: commonRu,
    organizations: organizationsRu,
    system: systemRu,
  },
} satisfies Record<AppLocale, I18nMessages>;

export async function loadMessages(locale: AppLocale): Promise<I18nMessages> {
  return messagesByLocale[locale];
}

export async function loadI18nMessagesConfig() {
  const locale = resolveAppLocale(process.env.PUBLIC_DEFAULT_LOCALE);

  return {
    locale,
    messages: await loadMessages(locale),
    timeZone: APP_TIME_ZONE,
  };
}
