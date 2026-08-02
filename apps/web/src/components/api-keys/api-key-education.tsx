"use client";

import { useTranslations } from "next-intl";
import { IconBuilding, IconKey, IconShieldLock } from "@tabler/icons-react";

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";

export function ApiKeyEducation() {
  const t = useTranslations("apiKeys.education");
  const items = [
    {
      icon: IconKey,
      title: t("personalTitle"),
      description: t("personalDescription"),
    },
    {
      icon: IconBuilding,
      title: t("organizationTitle"),
      description: t("organizationDescription"),
    },
    {
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
        {items.map(({ description, icon: Icon, title }) => (
          <Card key={title}>
            <CardHeader>
              <Icon aria-hidden="true" />
              <CardTitle>{title}</CardTitle>
            </CardHeader>
            <CardContent>
              <CardDescription>{description}</CardDescription>
            </CardContent>
          </Card>
        ))}
      </div>
    </section>
  );
}
