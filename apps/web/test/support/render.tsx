import { NextIntlClientProvider } from "next-intl";
import type { ReactNode } from "react";
import { render } from "@testing-library/react";

import account from "@/src/messages/account.en.json";
import apiKeys from "@/src/messages/api-keys.en.json";
import auth from "@/src/messages/auth.en.json";
import common from "@/src/messages/common.en.json";
import collaboration from "@/src/messages/collaboration.en.json";
import documents from "@/src/messages/documents.en.json";
import organizations from "@/src/messages/organizations.en.json";
import system from "@/src/messages/system.en.json";

export const englishMessages = {
  account,
  apiKeys,
  auth,
  collaboration,
  documents,
  common,
  organizations,
  system,
};

export function withMessages(children: ReactNode) {
  return (
    <NextIntlClientProvider
      locale="en"
      messages={englishMessages}
      timeZone="UTC"
    >
      {children}
    </NextIntlClientProvider>
  );
}

export function renderWithMessages(children: ReactNode) {
  return render(withMessages(children));
}
