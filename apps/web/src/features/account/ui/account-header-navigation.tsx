import Link from "next/link";
import { useTranslations } from "next-intl";

import { accountRoutes } from "@/src/features/account/account-routes";

export function AccountHeaderNavigation() {
  const t = useTranslations("account.navigation");

  return (
    <Link
      className="shrink-0 text-sm text-muted-foreground hover:text-foreground"
      href={accountRoutes.profile}
    >
      {t("label")}
    </Link>
  );
}
