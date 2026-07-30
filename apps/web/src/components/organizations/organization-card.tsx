import Link from "next/link";
import { useTranslations } from "next-intl";
import { IconArrowRight, IconSettings } from "@tabler/icons-react";

import { OrganizationDeleteDialog } from "@/src/components/organizations/organization-delete-dialog";
import { Badge } from "@/src/components/ui/badge";
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

export function OrganizationCard({
  canDelete = false,
  organization,
}: Readonly<{
  canDelete?: boolean;
  organization: OrganizationSummaryResponse;
}>) {
  const t = useTranslations("organizations.card");
  const roles = useTranslations("organizations.roles");

  return (
    <Card
      aria-label={t("accessibleName", { name: organization.name })}
      className="h-full"
      role="article"
    >
      <CardHeader className="min-w-0">
        <CardTitle className="min-w-0 truncate">{organization.name}</CardTitle>
        <CardAction>
          <Badge variant="outline">
            {t("roleLabel", { role: roles(organization.currentRole) })}
          </Badge>
        </CardAction>
      </CardHeader>
      <CardContent className="flex flex-1 items-center justify-between gap-3">
        <span className="text-muted-foreground">{t("slugLabel")}</span>
        <code className="truncate">{organization.slug}</code>
      </CardContent>
      <CardFooter className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
        {canDelete ? (
          <OrganizationDeleteDialog canDelete organization={organization} />
        ) : null}
        <Button asChild className="w-full sm:flex-1" variant="outline">
          <Link
            href={organizationRoutes.settingsWorkspace(
              organization.canonicalKey,
            )}
          >
            <IconSettings data-icon="inline-start" />
            {t("settings")}
          </Link>
        </Button>
        <Button asChild className="w-full sm:flex-1">
          <Link href={organizationRoutes.dashboard(organization.canonicalKey)}>
            {t("open")}
            <IconArrowRight data-icon="inline-end" />
          </Link>
        </Button>
      </CardFooter>
    </Card>
  );
}
