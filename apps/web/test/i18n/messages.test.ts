import {
  APP_TIME_ZONE,
  DEFAULT_LOCALE,
  isAppLocale,
  resolveAppLocale,
} from "@/src/i18n/config";
import { loadI18nMessagesConfig, loadMessages } from "@/src/i18n/messages";

describe("fixed deployment locale", () => {
  it.each([
    ["en", "en"],
    ["ru", "ru"],
    [undefined, "en"],
    ["de", "en"],
    ["", "en"],
  ] as const)("resolves %p to %p", (value, expected) => {
    expect(resolveAppLocale(value)).toBe(expected);
  });

  it("exposes only en and ru", () => {
    expect(DEFAULT_LOCALE).toBe("en");
    expect(APP_TIME_ZONE).toBe("UTC");
    expect(isAppLocale("en")).toBe(true);
    expect(isAppLocale("ru")).toBe(true);
    expect(isAppLocale("de")).toBe(false);
  });

  it("keeps the en and ru bundle shapes identical", async () => {
    const [english, russian] = await Promise.all([
      loadMessages("en"),
      loadMessages("ru"),
    ]);

    expect(Object.keys(russian.auth)).toEqual(Object.keys(english.auth));
    expect(Object.keys(russian.common)).toEqual(Object.keys(english.common));
    expect(Object.keys(russian.system)).toEqual(Object.keys(english.system));
    expect(russian.auth.login.title).not.toBe(english.auth.login.title);
    expect(russian.system.page.title).not.toBe(english.system.page.title);
  });

  it("loads the Russian deployment bundle and fixed UTC time zone", async () => {
    const originalLocale = process.env.PUBLIC_DEFAULT_LOCALE;

    process.env.PUBLIC_DEFAULT_LOCALE = "ru";

    try {
      const config = await loadI18nMessagesConfig();

      expect(config.locale).toBe("ru");
      expect(config.timeZone).toBe("UTC");
      expect(config.messages.system.page.title).toBe("REST-соединение");
    } finally {
      if (originalLocale === undefined) {
        delete process.env.PUBLIC_DEFAULT_LOCALE;
      } else {
        process.env.PUBLIC_DEFAULT_LOCALE = originalLocale;
      }
    }
  });
});
