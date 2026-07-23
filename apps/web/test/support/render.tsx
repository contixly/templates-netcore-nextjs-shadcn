import { NextIntlClientProvider } from "next-intl";
import type { ReactNode } from "react";
import { render } from "@testing-library/react";

import common from "@/src/messages/common.en.json";
import system from "@/src/messages/system.en.json";

export const englishMessages = { common, system };

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
