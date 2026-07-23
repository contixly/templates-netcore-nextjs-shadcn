import {
  APP_TIME_ZONE,
  DEFAULT_LOCALE,
  isAppLocale,
  resolveAppLocale,
} from "@/src/i18n/config";
import { loadMessages } from "@/src/i18n/messages";

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

    expect(Object.keys(russian.common)).toEqual(Object.keys(english.common));
    expect(Object.keys(russian.system)).toEqual(Object.keys(english.system));
    expect(russian.system.page.title).not.toBe(english.system.page.title);
  });
});
