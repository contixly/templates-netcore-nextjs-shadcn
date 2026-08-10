"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import {
  IconBuilding,
  IconKey,
  IconShieldCheck,
  IconUser,
} from "@tabler/icons-react";

import { Button } from "@/src/components/ui/button";
import { SettingsSection } from "@/src/features/application/ui/settings/settings-shell";
import type { ApiKeyOwner } from "@/src/features/api-keys/api-key-routes";
import { apiKeyRoutes } from "@/src/features/api-keys/api-key-routes";

export function ApiKeyEducation({
  headingLevel = 2,
  owner,
}: Readonly<{ headingLevel?: 2 | 3; owner: ApiKeyOwner }>) {
  const t = useTranslations("apiKeys.education");

  return (
    <SettingsSection
      action={
        owner.kind === "organization" ? (
          <Button asChild size="sm" variant="outline">
            <Link href={apiKeyRoutes.personal}>
              <IconKey data-icon="inline-start" />
              {t("personalAction")}
            </Link>
          </Button>
        ) : null
      }
      description={t("description")}
      headingLevel={headingLevel}
      title={t("title")}
    >
      <div className="grid gap-4 md:grid-cols-2">
        <EducationItem
          description={t("personalDescription")}
          icon={<IconUser aria-hidden="true" className="size-4" />}
          title={t("personalTitle")}
        />
        <EducationItem
          description={t("organizationDescription")}
          icon={<IconBuilding aria-hidden="true" className="size-4" />}
          title={t("organizationTitle")}
        />
        <EducationItem
          description={t("scopesDescription")}
          icon={<IconShieldCheck aria-hidden="true" className="size-4" />}
          title={t("scopesTitle")}
        />
        <EducationItem
          description={t("managementDescription")}
          icon={<IconKey aria-hidden="true" className="size-4" />}
          title={t("managementTitle")}
        />
      </div>
    </SettingsSection>
  );
}

function EducationItem({
  description,
  icon,
  title,
}: Readonly<{
  description: string;
  icon: ReactNode;
  title: string;
}>) {
  return (
    <div className="flex min-w-0 gap-3">
      <div className="flex size-8 shrink-0 items-center justify-center rounded-none bg-muted text-muted-foreground">
        {icon}
      </div>
      <div className="flex min-w-0 flex-col gap-1">
        <h3 className="text-sm font-medium">{title}</h3>
        <p className="text-sm text-muted-foreground">{description}</p>
      </div>
    </div>
  );
}
