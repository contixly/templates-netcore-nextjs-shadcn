"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { IconBuilding, IconKey, IconShieldLock } from "@tabler/icons-react";

import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import { Button } from "@/src/components/ui/button";
import type { ApiKeyOwner } from "@/src/features/api-keys/api-key-routes";
import { apiKeyRoutes } from "@/src/features/api-keys/api-key-routes";

export function ApiKeyEducation({ owner }: Readonly<{ owner: ApiKeyOwner }>) {
  const t = useTranslations("apiKeys.education");
  const items = [
    {
      action: owner.kind === "organization" ? t("personalAction") : undefined,
      href: owner.kind === "organization" ? apiKeyRoutes.personal : undefined,
      id: "personal",
      icon: IconKey,
      title: t("personalTitle"),
      description: t("personalDescription"),
    },
    {
      id: "organization",
      icon: IconBuilding,
      title: t("organizationTitle"),
      description: t("organizationDescription"),
    },
    {
      id: "security",
      icon: IconShieldLock,
      title: t("securityTitle"),
      description: t("securityDescription"),
    },
  ];

  return (
    <section
      aria-labelledby="api-key-education-title"
      className="flex flex-col gap-3"
    >
      <h2 className="text-lg font-medium" id="api-key-education-title">
        {t("title")}
      </h2>
      <div className="grid gap-3 lg:grid-cols-3">
        {items.map(({ action, description, href, icon: Icon, id, title }) => (
          <Card key={id}>
            <CardHeader>
              <Icon aria-hidden="true" />
              <CardTitle>{title}</CardTitle>
            </CardHeader>
            <CardContent>
              <CardDescription>{description}</CardDescription>
            </CardContent>
            {href && action ? (
              <CardFooter>
                <Button asChild size="sm" variant="outline">
                  <Link href={href}>{action}</Link>
                </Button>
              </CardFooter>
            ) : null}
          </Card>
        ))}
      </div>
    </section>
  );
}
