import Link from "next/link";
import { useTranslations } from "next-intl";
import { IconUserCog } from "@tabler/icons-react";

import { OrganizationCreateDialog } from "@/src/components/organizations/organization-create-dialog";
import { Button } from "@/src/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import { accountRoutes } from "@/src/features/account/account-routes";

export function OrganizationOnboarding() {
  const t = useTranslations("organizations.onboarding");

  return (
    <main className="mx-auto flex w-full max-w-5xl flex-1 items-center justify-center px-4 py-12">
      <Card className="w-full max-w-2xl">
        <CardHeader className="text-center">
          <CardTitle className="text-2xl">
            <h1>{t("title")}</h1>
          </CardTitle>
          <CardDescription>{t("description")}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col justify-center gap-3 sm:flex-row">
          <OrganizationCreateDialog presentation="onboarding" />
          <Button asChild size="lg" variant="outline">
            <Link href={accountRoutes.profile}>
              <IconUserCog data-icon="inline-start" />
              {t("accountAction")}
            </Link>
          </Button>
        </CardContent>
      </Card>
    </main>
  );
}
