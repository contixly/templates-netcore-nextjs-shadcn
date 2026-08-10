import Link from "next/link";
import { useTranslations } from "next-intl";
import { IconMail } from "@tabler/icons-react";

import { AccountInvitationList } from "@/src/features/collaboration/ui/account-invitation-list";
import { OrganizationCreateDialog } from "@/src/features/organizations/ui/organization-create-dialog";
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
    <section className="flex w-full flex-1 items-center justify-center px-4 py-8 lg:px-6">
      <div className="flex w-full max-w-2xl flex-col gap-6">
        <Card className="w-full shadow-none">
          <CardHeader className="gap-3 text-center">
            <CardTitle className="text-2xl">
              <h1>{t("title")}</h1>
            </CardTitle>
            <CardDescription className="text-base">
              {t("description")}
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col items-center justify-center gap-3 sm:flex-row sm:flex-wrap">
            <OrganizationCreateDialog presentation="onboarding" />
            <Button
              asChild
              className="h-auto min-h-9 py-2 whitespace-normal"
              size="lg"
              variant="outline"
            >
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
    </section>
  );
}
