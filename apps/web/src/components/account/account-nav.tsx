"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";

import { accountRoutes } from "@/src/features/account/account-routes";
import { cn } from "@/src/lib/utils";

const accountNavigation = [
  { href: accountRoutes.profile, label: "profile", destructive: false },
  {
    href: accountRoutes.connections,
    label: "connections",
    destructive: false,
  },
  { href: accountRoutes.security, label: "security", destructive: false },
  {
    href: accountRoutes.invitations,
    label: "invitations",
    destructive: false,
  },
  { href: accountRoutes.danger, label: "danger", destructive: true },
] as const;

function isActiveAccountRoute(pathname: string, href: string): boolean {
  return pathname === href || pathname.startsWith(`${href}/`);
}

export function AccountNav({
  pathname: pathnameOverride,
}: Readonly<{ pathname?: string }>) {
  if (pathnameOverride !== undefined) {
    return <AccountNavLinks pathname={pathnameOverride} />;
  }

  return <CurrentAccountNav />;
}

function CurrentAccountNav() {
  return <AccountNavLinks pathname={usePathname()} />;
}

function AccountNavLinks({ pathname }: Readonly<{ pathname: string }>) {
  const t = useTranslations("account.navigation");

  return (
    <nav
      aria-label={t("label")}
      className="w-full shrink-0 border-b md:w-56 md:border-r md:border-b-0"
    >
      <ul className="flex gap-1 overflow-x-auto p-2 md:flex-col md:overflow-visible">
        {accountNavigation.map((item) => {
          const active = isActiveAccountRoute(pathname, item.href);

          return (
            <li className="min-w-max md:min-w-0" key={item.href}>
              <Link
                aria-current={active ? "page" : undefined}
                className={cn(
                  "flex min-h-10 items-center px-3 py-2 text-sm font-medium text-muted-foreground hover:bg-accent hover:text-accent-foreground md:w-full",
                  active && "bg-accent text-accent-foreground",
                  item.destructive && !active && "text-destructive",
                )}
                href={item.href}
              >
                {t(item.label)}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
