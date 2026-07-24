import { getRequestConfig } from "next-intl/server";

import { loadI18nMessagesConfig } from "@/src/i18n/messages";

export default getRequestConfig(loadI18nMessagesConfig);
