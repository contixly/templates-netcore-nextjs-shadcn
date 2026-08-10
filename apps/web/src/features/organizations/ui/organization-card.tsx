import Link from "next/link";
import { useTranslations } from "next-intl";
import { IconSettings } from "@tabler/icons-react";

import { OrganizationDeleteDialog } from "@/src/features/organizations/ui/organization-delete-dialog";
import { Button } from "@/src/components/ui/button";
import {
  Card,
  CardAction,
  CardContent,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import type { OrganizationSummaryResponse } from "@/src/lib/api/generated/types.gen";

type UserOrganizationSummary = Extract<
  OrganizationSummaryResponse,
  { accessPrincipal: "user" }
>;

export type OrganizationCardView = Pick<
  UserOrganizationSummary,
  "canonicalKey" | "currentRole" | "id" | "name" | "slug"
> &
  Readonly<{
    capabilities: Readonly<{ canDeleteOrganization: boolean }>;
  }>;

export function OrganizationCard({
  canDelete = false,
  onDeleted,
  organization,
}: Readonly<{
  canDelete?: boolean;
  onDeleted?: (organizationId: string) => void | Promise<void>;
  organization: OrganizationCardView;
}>) {
  const t = useTranslations("organizations.card");

  return (
    <Card
      aria-label={t("accessibleName", { name: organization.name })}
      className="h-full w-full max-w-md min-w-0 shadow-none transition-shadow"
      role="article"
    >
      <CardHeader className="min-w-0">
        <CardTitle className="min-w-0 truncate">{organization.name}</CardTitle>
        <CardAction>
          <Button asChild size="icon" variant="ghost">
            <Link
              aria-label={t("settings")}
              href={organizationRoutes.settingsWorkspace(
                organization.canonicalKey,
              )}
            >
              <IconSettings aria-hidden="true" />
              <span className="sr-only">{t("settings")}</span>
            </Link>
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent className="flex flex-1 flex-col gap-4 text-sm">
        <div className="flex items-center justify-between gap-3">
          <span className="text-muted-foreground">{t("slugLabel")}</span>
          <code className="max-w-[70%] truncate bg-muted px-2 py-1 text-xs">
            {organization.slug}
          </code>
        </div>
        <p className="text-muted-foreground">{t("summary")}</p>
      </CardContent>
      <CardFooter className="flex flex-col gap-2">
        <Button asChild className="w-full" variant="outline">
          <Link href={organizationRoutes.dashboard(organization.canonicalKey)}>
            {t("open")}
          </Link>
        </Button>
        {canDelete ? (
          <OrganizationDeleteDialog
            canDelete
            onDeleted={onDeleted}
            organization={{ id: organization.id, name: organization.name }}
            triggerClassName="w-full"
          />
        ) : null}
      </CardFooter>
    </Card>
  );
}
