"use client";

import { useTranslations } from "next-intl";
import { useState } from "react";

import { Alert, AlertTitle } from "@/src/components/ui/alert";
import { Button } from "@/src/components/ui/button";
import { collaborationRoutes } from "@/src/features/collaboration/collaboration-routes";

export function invitationAbsoluteUrl(
  invitationId: string,
  invitationPath: string,
): string | null {
  if (typeof window === "undefined") return null;
  const expectedPath = collaborationRoutes.invitationDecision(invitationId);
  try {
    const value = new URL(invitationPath, window.location.origin);
    return value.origin === window.location.origin &&
      value.pathname === expectedPath &&
      value.username === "" &&
      value.password === "" &&
      value.search === "" &&
      value.hash === ""
      ? value.href
      : null;
  } catch {
    return null;
  }
}

export function InvitationCopyButton({
  invitationId,
  invitationPath,
  onInvalid,
}: Readonly<{
  invitationId: string;
  invitationPath: string;
  onInvalid?: () => void;
}>) {
  const t = useTranslations("collaboration.invitations.create");
  const [notice, setNotice] = useState<"copied" | "failed" | null>(null);

  async function copy() {
    const value = invitationAbsoluteUrl(invitationId, invitationPath);
    if (!value) {
      setNotice("failed");
      onInvalid?.();
      return;
    }
    try {
      await navigator.clipboard.writeText(value);
      setNotice("copied");
    } catch {
      setNotice("failed");
    }
  }

  return (
    <div className="flex flex-col items-start gap-2">
      <Button onClick={copy} size="sm" type="button" variant="outline">
        {t("copy")}
      </Button>
      {notice ? (
        <Alert variant={notice === "failed" ? "destructive" : "default"}>
          <AlertTitle>
            {notice === "copied" ? t("copied") : t("copyFailure")}
          </AlertTitle>
        </Alert>
      ) : null}
    </div>
  );
}
