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

    expect(Object.keys(russian.account)).toEqual(Object.keys(english.account));
    expect(Object.keys(russian.auth)).toEqual(Object.keys(english.auth));
    expect(Object.keys(russian.common)).toEqual(Object.keys(english.common));
    expect(Object.keys(russian.organizations)).toEqual(
      Object.keys(english.organizations),
    );
    expect(Object.keys(russian.system)).toEqual(Object.keys(english.system));
    expect(russian.auth.login.title).not.toBe(english.auth.login.title);
    expect(russian.system.page.title).not.toBe(english.system.page.title);
  });

  it("keeps every English and Russian organization message at the same path", async () => {
    const [english, russian] = await Promise.all([
      loadMessages("en"),
      loadMessages("ru"),
    ]);

    const messagePaths = (value: unknown, prefix = ""): string[] =>
      Object.entries(value as Record<string, unknown>).flatMap(
        ([key, child]) => {
          const path = prefix ? `${prefix}.${key}` : key;
          return typeof child === "object" && child !== null
            ? messagePaths(child, path)
            : [path];
        },
      );

    expect(messagePaths(russian.organizations).sort()).toEqual(
      messagePaths(english.organizations).sort(),
    );
    expect(russian.organizations.onboarding.title).not.toBe(
      english.organizations.onboarding.title,
    );
    expect(russian.organizations.switcher.failure).not.toBe(
      english.organizations.switcher.failure,
    );
  });

  it("keeps every English and Russian account message at the same path", async () => {
    const [english, russian] = await Promise.all([
      loadMessages("en"),
      loadMessages("ru"),
    ]);

    const messagePaths = (value: unknown, prefix = ""): string[] =>
      Object.entries(value as Record<string, unknown>).flatMap(
        ([key, child]) => {
          const path = prefix ? `${prefix}.${key}` : key;
          return typeof child === "object" && child !== null
            ? messagePaths(child, path)
            : [path];
        },
      );

    expect(messagePaths(russian.account).sort()).toEqual(
      messagePaths(english.account).sort(),
    );
    expect(russian.account.pages.profile.title).not.toBe(
      english.account.pages.profile.title,
    );
    expect(russian.account.deleteAccount.confirmationLabel).not.toBe(
      english.account.deleteAccount.confirmationLabel,
    );
    expect(russian.account.sessions.authenticationMethods.local).not.toBe(
      english.account.sessions.authenticationMethods.local,
    );
  });

  it("provides complete localized external-auth and safe error copy", async () => {
    const [english, russian] = await Promise.all([
      loadMessages("en"),
      loadMessages("ru"),
    ]);
    const expectedErrorCodes = [
      "alreadyAuthenticated",
      "externalAuthFailed",
      "externalEmailConflict",
      "externalEmailRequired",
      "externalEmailUnverified",
      "externalIdentityConflict",
      "externalProviderNotConfigured",
      "generic",
      "invalidReturnUrl",
      "oauthFlowContextChanged",
    ];

    expect(Object.keys(english.auth.error.codes).sort()).toEqual(
      expectedErrorCodes,
    );
    expect(Object.keys(russian.auth.error.codes).sort()).toEqual(
      expectedErrorCodes,
    );
    expect(russian.auth.externalProviders.button).not.toBe(
      english.auth.externalProviders.button,
    );
    expect(russian.auth.error.codes.generic.title).not.toBe(
      english.auth.error.codes.generic.title,
    );
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
