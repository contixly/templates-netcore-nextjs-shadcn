import Link from "next/link";
import { useTranslations } from "next-intl";
import { IconMail, IconUserCog } from "@tabler/icons-react";

import { AccountInvitationList } from "@/src/components/collaboration/account-invitation-list";
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
import type { AccountInvitationPageResponse } from "@/src/lib/api/generated/types.gen";

export function OrganizationOnboarding({
  initialInvitations,
}: Readonly<{
  initialInvitations?: AccountInvitationPageResponse;
}> = {}) {
  const t = useTranslations("organizations.onboarding");

  return (
    <main className="mx-auto flex w-full max-w-5xl flex-1 items-center justify-center px-4 py-12">
      <div className="flex w-full max-w-2xl flex-col gap-6">
        <Card className="w-full">
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
            <Button asChild size="lg" variant="outline">
              <Link href={accountRoutes.invitations}>
                <IconMail data-icon="inline-start" />
                {t("reviewInvitationsAction")}
              </Link>
            </Button>
          </CardContent>
        </Card>
        {initialInvitations ? (
          <AccountInvitationList
            initialPage={initialInvitations}
            showEmptyState={false}
          />
        ) : null}
      </div>
    </main>
  );
}
