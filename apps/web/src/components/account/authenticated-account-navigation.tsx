import "server-only";

import { connection } from "next/server";

import { AccountHeaderNavigation } from "@/src/components/account/account-header-navigation";
import { BrowserSessionRefresh } from "@/src/components/authentication/browser-session-refresh";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";

export async function AuthenticatedAccountNavigation() {
  await connection();
  const session = await loadServerAuthSession();

  if (
    !session.ok ||
    session.data.authenticated !== true ||
    !session.data.session ||
    !session.data.user
  ) {
    return null;
  }

  return (
    <>
      <BrowserSessionRefresh />
      <AccountHeaderNavigation />
    </>
  );
}
