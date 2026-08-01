"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { useInsertionEffect, useLayoutEffect, useRef, useState } from "react";

import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import { Badge } from "@/src/components/ui/badge";
import { Button } from "@/src/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import { collaborationRoutes } from "@/src/features/collaboration/collaboration-routes";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { confirmLocalAutomationEmail } from "@/src/lib/api/auth/browser/confirm-local-automation-email";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import {
  acceptBrowserInvitation,
  rejectBrowserInvitation,
} from "@/src/lib/api/collaboration/browser/collaboration-mutations";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getInvitationDecision } from "@/src/lib/api/generated/sdk.gen";
import type {
  InvitationDecisionResponse,
  InvitationResponse,
} from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

type PendingAction = "accept" | "confirm" | "reject" | "refresh" | null;

function formattedDate(value: string, locale: string): string {
  const date = new Date(value);
  return Number.isNaN(date.valueOf())
    ? value
    : new Intl.DateTimeFormat(locale, {
        dateStyle: "medium",
        timeStyle: "short",
        timeZone: "UTC",
      }).format(date);
}

function useInvitationLifecycle(currentInvitationId: string | null) {
  const attached = useRef(true);
  const visible = useRef(true);
  const currentId = useRef(currentInvitationId);
  const queued = useRef<Readonly<{ id: string; run: () => void }> | null>(null);

  useInsertionEffect(() => {
    attached.current = true;
    currentId.current = currentInvitationId;
    queued.current = null;
    return () => {
      attached.current = false;
      queued.current = null;
    };
  }, [currentInvitationId]);
  useLayoutEffect(() => {
    visible.current = true;
    const effect = queued.current;
    queued.current = null;
    if (effect && attached.current && currentId.current === effect.id) {
      effect.run();
    }
    return () => {
      visible.current = false;
    };
  });

  return {
    isCurrent(invitationId: string) {
      return attached.current && currentId.current === invitationId;
    },
    runOrQueue(invitationId: string, run: () => void) {
      if (!attached.current || currentId.current !== invitationId) return;
      if (visible.current) run();
      else queued.current = { id: invitationId, run };
    },
  };
}

function stableFailureMessage(
  failure: ApiFailure,
  translate: ReturnType<typeof useTranslations<"collaboration.failures">>,
) {
  if (failure.kind !== "problem") return translate("generic");
  return (
    {
      antiforgery_failed: translate("codes.antiforgery_failed"),
      invitation_domain_restricted: translate(
        "codes.invitation_domain_restricted",
      ),
      invitation_email_verification_required: translate(
        "codes.invitation_email_verification_required",
      ),
      invitation_expired: translate("codes.invitation_expired"),
      invitation_membership_conflict: translate(
        "codes.invitation_membership_conflict",
      ),
      invitation_not_found: translate("codes.invitation_not_found"),
      invitation_not_pending: translate("codes.invitation_not_pending"),
      invitation_recipient_mismatch: translate(
        "codes.invitation_recipient_mismatch",
      ),
      rate_limited: translate("codes.rate_limited"),
    }[failure.code] ?? translate("generic")
  );
}

function DecisionFailure({
  failure,
}: Readonly<{ failure: ApiFailure | null }>) {
  const t = useTranslations("collaboration.failures");
  if (!failure) return null;
  return (
    <Alert variant="destructive">
      <AlertTitle>{stableFailureMessage(failure, t)}</AlertTitle>
      {failure.kind === "problem" && failure.traceId ? (
        <AlertDescription>
          {t("trace", { traceId: failure.traceId })}
        </AlertDescription>
      ) : null}
    </Alert>
  );
}

export function InvitationDecision({
  decision: serverDecision,
  emailVerified,
  localEmailConfirmationAvailable,
}: Readonly<{
  decision: InvitationDecisionResponse;
  emailVerified: boolean;
  localEmailConfirmationAvailable: boolean;
}>) {
  const t = useTranslations("collaboration.decision");
  const roles = useTranslations("collaboration.invitations.roles");
  const locale = useLocale();
  const router = useRouter();
  const serverIdentity = serverDecision.invitation?.id ?? null;
  const lifecycle = useInvitationLifecycle(serverIdentity);
  const [lastServerDecision, setLastServerDecision] = useState(serverDecision);
  const [decision, setDecision] = useState(serverDecision);
  const [lastEmailVerified, setLastEmailVerified] = useState(emailVerified);
  const [verified, setVerified] = useState(emailVerified);
  const [pendingAction, setPendingAction] = useState<PendingAction>(null);
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [refreshFailure, setRefreshFailure] = useState(false);
  const mutationInFlight = useRef(false);

  useInsertionEffect(() => {
    mutationInFlight.current = false;
  }, [serverIdentity]);

  if (lastServerDecision !== serverDecision) {
    setLastServerDecision(serverDecision);
    setDecision(serverDecision);
    setPendingAction(null);
    setFailure(null);
    setRefreshFailure(false);
  }
  if (lastEmailVerified !== emailVerified) {
    setLastEmailVerified(emailVerified);
    setVerified(emailVerified);
  }

  const invitation =
    decision.state === "recipient-mismatch" ? null : decision.invitation;
  const actionable =
    verified &&
    decision.state === "pending" &&
    decision.canRespond &&
    invitation !== null;

  function isCurrent(invitationId: string) {
    return lifecycle.isCurrent(invitationId);
  }

  function safelyRunOrQueue(invitationId: string, run: () => void) {
    lifecycle.runOrQueue(invitationId, run);
  }

  async function refreshDecision(invitationId: string) {
    if (!isCurrent(invitationId)) return false;
    setPendingAction("refresh");
    try {
      const result = await getInvitationDecision({
        client: createBrowserApiClient(),
        cache: "no-store",
        path: { invitationId },
      });
      if (!isCurrent(invitationId)) return false;
      if (result.data === undefined) {
        normalizeApiFailure(result.error, result.response);
        setRefreshFailure(true);
        return false;
      }
      const refreshed = result.data.data;
      if (
        refreshed.invitation !== null &&
        refreshed.invitation.id !== invitationId
      ) {
        setRefreshFailure(true);
        return false;
      }
      setDecision(refreshed);
      setRefreshFailure(false);
      safelyRunOrQueue(invitationId, () => {
        if (lifecycle.isCurrent(invitationId)) router.refresh();
      });
      return true;
    } catch (error) {
      normalizeApiFailure(error);
      if (isCurrent(invitationId)) setRefreshFailure(true);
      return false;
    } finally {
      if (isCurrent(invitationId)) setPendingAction(null);
    }
  }

  async function accept() {
    if (!actionable || !invitation || mutationInFlight.current) return;
    const invitationId = invitation.id;
    mutationInFlight.current = true;
    setPendingAction("accept");
    setFailure(null);
    const result = await acceptBrowserInvitation(
      createBrowserApiClient(),
      invitationId,
    );
    if (!isCurrent(invitationId)) return;
    mutationInFlight.current = false;
    setPendingAction(null);
    if (!result.ok) {
      setFailure(result.failure);
      return;
    }
    if (result.data.invitationId !== invitationId) {
      setFailure({ kind: "network", code: "api_unavailable" });
      return;
    }
    safelyRunOrQueue(invitationId, () => {
      if (!lifecycle.isCurrent(invitationId)) return;
      router.replace(
        organizationRoutes.dashboard(result.data.canonicalOrganizationKey),
      );
    });
  }

  async function reject() {
    if (!actionable || !invitation || mutationInFlight.current) return;
    const invitationId = invitation.id;
    mutationInFlight.current = true;
    setPendingAction("reject");
    setFailure(null);
    setRefreshFailure(false);
    const result = await rejectBrowserInvitation(
      createBrowserApiClient(),
      invitationId,
    );
    if (!isCurrent(invitationId)) return;
    mutationInFlight.current = false;
    setPendingAction(null);
    if (!result.ok) {
      setFailure(result.failure);
      return;
    }
    if (
      result.data.state !== "rejected" ||
      (result.data.invitation !== null &&
        result.data.invitation.id !== invitationId)
    ) {
      setFailure({ kind: "network", code: "api_unavailable" });
      return;
    }
    const rejectedInvitation: InvitationResponse = result.data.invitation ?? {
      ...invitation,
      status: "rejected",
      displayState: "rejected",
    };
    setDecision({
      invitation: rejectedInvitation,
      state: "rejected",
      canRespond: false,
    });
    await refreshDecision(invitationId);
  }

  async function confirmEmail() {
    if (
      !localEmailConfirmationAvailable ||
      decision.state !== "email-verification-required" ||
      !invitation ||
      mutationInFlight.current
    ) {
      return;
    }
    const invitationId = invitation.id;
    mutationInFlight.current = true;
    setPendingAction("confirm");
    setFailure(null);
    const result = await confirmLocalAutomationEmail(createBrowserApiClient());
    if (!isCurrent(invitationId)) return;
    mutationInFlight.current = false;
    setPendingAction(null);
    if (!result.ok || result.data.user?.emailVerified !== true) {
      setFailure(
        result.ok
          ? { kind: "network", code: "api_unavailable" }
          : result.failure,
      );
      return;
    }
    setVerified(true);
    await refreshDecision(invitationId);
  }

  const stateMessageKey = {
    pending: "states.pending",
    accepted: "states.accepted",
    rejected: "states.rejected",
    canceled: "states.canceled",
    expired: "states.expired",
    "recipient-mismatch": "states.recipientMismatch",
    "email-verification-required": "states.emailVerificationRequired",
    "domain-restricted": "states.domainRestricted",
    "already-member": "states.alreadyMember",
  }[decision.state] as Parameters<typeof t>[0];

  return (
    <Card className="w-full">
      <CardHeader>
        <CardTitle>{t("page.title")}</CardTitle>
        <CardDescription>
          {t(
            stateMessageKey,
            invitation ? { organization: invitation.organizationName } : {},
          )}
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-6">
        {invitation ? (
          <dl className="grid gap-4 sm:grid-cols-2">
            <div>
              <dt className="text-muted-foreground">
                {t("details.workspace")}
              </dt>
              <dd className="font-medium">{invitation.organizationName}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t("details.email")}</dt>
              <dd className="font-medium">{invitation.email}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t("details.role")}</dt>
              <dd>
                <Badge variant="secondary">{roles(invitation.role)}</Badge>
              </dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t("details.team")}</dt>
              <dd className="font-medium">
                {invitation.teamName ?? t("details.noTeam")}
              </dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t("details.inviter")}</dt>
              <dd className="font-medium">{invitation.inviterName}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t("details.expires")}</dt>
              <dd className="font-medium">
                {formattedDate(invitation.expiresAt, locale)}
              </dd>
            </div>
          </dl>
        ) : null}

        {decision.state === "email-verification-required" &&
        localEmailConfirmationAvailable &&
        invitation ? (
          <Alert>
            <AlertTitle>{t("localConfirmation.warning")}</AlertTitle>
            <AlertDescription>
              <Button
                disabled={pendingAction !== null}
                onClick={confirmEmail}
                type="button"
                variant="outline"
              >
                {pendingAction === "confirm"
                  ? t("localConfirmation.confirming")
                  : t("localConfirmation.confirm")}
              </Button>
            </AlertDescription>
          </Alert>
        ) : null}

        <DecisionFailure failure={failure} />
        {refreshFailure ? (
          <Alert variant="destructive">
            <AlertTitle>{t("success.refreshFailure")}</AlertTitle>
            <AlertDescription>
              <Button
                disabled={pendingAction !== null}
                onClick={() =>
                  invitation && void refreshDecision(invitation.id)
                }
                type="button"
                variant="outline"
              >
                {pendingAction === "refresh"
                  ? t("actions.refreshing")
                  : t("actions.retry")}
              </Button>
            </AlertDescription>
          </Alert>
        ) : null}

        <div className="flex flex-wrap gap-2">
          {actionable ? (
            <>
              <Button
                disabled={pendingAction !== null}
                onClick={accept}
                type="button"
              >
                {pendingAction === "accept"
                  ? t("actions.accepting")
                  : t("actions.accept")}
              </Button>
              <Button
                disabled={pendingAction !== null}
                onClick={reject}
                type="button"
                variant="outline"
              >
                {pendingAction === "reject"
                  ? t("actions.rejecting")
                  : t("actions.reject")}
              </Button>
            </>
          ) : null}
          {decision.state === "already-member" && invitation ? (
            <Button asChild variant="outline">
              <Link
                href={organizationRoutes.dashboard(
                  invitation.canonicalOrganizationKey,
                )}
              >
                {t("actions.openWorkspace")}
              </Link>
            </Button>
          ) : null}
          <Button asChild variant="ghost">
            <Link href={collaborationRoutes.accountInvitations}>
              {t("actions.viewInvitations")}
            </Link>
          </Button>
        </div>
        {decision.state === "rejected" ? (
          <p role="status">{t("success.rejected")}</p>
        ) : null}
      </CardContent>
    </Card>
  );
}
