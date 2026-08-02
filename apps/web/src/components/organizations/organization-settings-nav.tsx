"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";

import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { cn } from "@/src/lib/utils";

function OrganizationSettingsNavLinks({
  canManageApiKeys,
  canManageInvitations,
  organizationKey,
  pathname,
}: Readonly<{
  canManageApiKeys: boolean;
  canManageInvitations: boolean;
  organizationKey: string;
  pathname: string;
}>) {
  const t = useTranslations("organizations.settings.navigation");
  const items = [
    {
      href: organizationRoutes.settingsWorkspace(organizationKey),
      label: "workspace",
    },
    {
      href: organizationRoutes.settingsUsers(organizationKey),
      label: "users",
    },
    {
      href: organizationRoutes.settingsRoles(organizationKey),
      label: "roles",
    },
    {
      href: organizationRoutes.settingsTeams(organizationKey),
      label: "teams",
    },
    ...(canManageInvitations
      ? [
          {
            href: organizationRoutes.settingsInvitations(organizationKey),
            label: "invitations" as const,
          },
        ]
      : []),
    ...(canManageApiKeys
      ? [
          {
            href: organizationRoutes.settingsApiKeys(organizationKey),
            label: "apiKeys" as const,
          },
        ]
      : []),
  ] as const;

  return (
    <nav
      aria-label={t("label")}
      className="w-full shrink-0 border-b md:w-56 md:border-r md:border-b-0"
    >
      <ul className="flex gap-1 overflow-x-auto p-2 md:flex-col md:overflow-visible">
        {items.map((item) => {
          const active =
            pathname === item.href || pathname.startsWith(`${item.href}/`);

          return (
            <li className="min-w-max md:min-w-0" key={item.href}>
              <Link
                aria-current={active ? "page" : undefined}
                className={cn(
                  "flex min-h-10 items-center px-3 py-2 text-sm font-medium text-muted-foreground hover:bg-accent hover:text-accent-foreground md:w-full",
                  active && "bg-accent text-accent-foreground",
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

function CurrentOrganizationSettingsNav({
  canManageApiKeys,
  canManageInvitations,
  organizationKey,
}: Readonly<{
  canManageApiKeys: boolean;
  canManageInvitations: boolean;
  organizationKey: string;
}>) {
  return (
    <OrganizationSettingsNavLinks
      canManageApiKeys={canManageApiKeys}
      canManageInvitations={canManageInvitations}
      organizationKey={organizationKey}
      pathname={usePathname()}
    />
  );
}

export function OrganizationSettingsNav({
  canManageApiKeys,
  canManageInvitations,
  organizationKey,
  pathname,
}: Readonly<{
  canManageApiKeys: boolean;
  canManageInvitations: boolean;
  organizationKey: string;
  pathname?: string;
}>) {
  return pathname === undefined ? (
    <CurrentOrganizationSettingsNav
      canManageApiKeys={canManageApiKeys}
      canManageInvitations={canManageInvitations}
      organizationKey={organizationKey}
    />
  ) : (
    <OrganizationSettingsNavLinks
      canManageApiKeys={canManageApiKeys}
      canManageInvitations={canManageInvitations}
      organizationKey={organizationKey}
      pathname={pathname}
    />
  );
}
