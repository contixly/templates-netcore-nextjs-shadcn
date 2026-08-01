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
import {
  failureIsRepresentedByDecision,
  isInvitationNotPendingFailure,
  recipientMismatchDecision,
  sanitizeInvitationDecision,
  terminalInvitationDecision,
} from "@/src/features/collaboration/invitation-decision-failure";
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
type RefreshFailure = "not-pending" | "reconcile" | "saved" | null;

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
  messageCoveredByState,
}: Readonly<{
  failure: ApiFailure | null;
  messageCoveredByState: boolean;
}>) {
  const t = useTranslations("collaboration.failures");
  if (!failure) return null;
  const traceId = failure.kind === "problem" ? failure.traceId : undefined;
  if (messageCoveredByState && !traceId) return null;
  return (
    <Alert variant="destructive">
      {messageCoveredByState ? null : (
        <AlertTitle>{stableFailureMessage(failure, t)}</AlertTitle>
      )}
      {traceId ? (
        <AlertDescription>{t("trace", { traceId })}</AlertDescription>
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
  const safeServerDecision = sanitizeInvitationDecision(serverDecision);
  const serverIdentity = safeServerDecision.invitation?.id ?? null;
  const lifecycle = useInvitationLifecycle(serverIdentity);
  const [lastServerDecision, setLastServerDecision] = useState(serverDecision);
  const [decision, setDecision] = useState(safeServerDecision);
  const [lastEmailVerified, setLastEmailVerified] = useState(emailVerified);
  const [verified, setVerified] = useState(emailVerified);
  const [pendingAction, setPendingAction] = useState<PendingAction>(null);
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [refreshFailure, setRefreshFailure] = useState<RefreshFailure>(null);
  const mutationInFlight = useRef(false);

  useInsertionEffect(() => {
    mutationInFlight.current = false;
  }, [serverIdentity]);

  if (lastServerDecision !== serverDecision) {
    setLastServerDecision(serverDecision);
    setDecision(safeServerDecision);
    setPendingAction(null);
    setFailure(null);
    setRefreshFailure(null);
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

  async function refreshDecision(
    invitationId: string,
    failureKind: Exclude<RefreshFailure, null> = "saved",
  ) {
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
        const normalized = normalizeApiFailure(result.error, result.response);
        const mismatch = recipientMismatchDecision(normalized);
        if (mismatch) {
          setDecision(mismatch);
          setFailure(null);
          setRefreshFailure(null);
          return false;
        }
        setRefreshFailure(failureKind);
        return false;
      }
      const refreshed = sanitizeInvitationDecision(result.data.data);
      if (failureKind === "not-pending" && refreshed.state === "pending") {
        setRefreshFailure("not-pending");
        return false;
      }
      if (
        refreshed.invitation !== null &&
        refreshed.invitation.id !== invitationId
      ) {
        setRefreshFailure(failureKind);
        return false;
      }
      setDecision(refreshed);
      setFailure(null);
      setRefreshFailure(null);
      safelyRunOrQueue(invitationId, () => {
        if (lifecycle.isCurrent(invitationId)) router.refresh();
      });
      return true;
    } catch (error) {
      normalizeApiFailure(error);
      if (isCurrent(invitationId)) setRefreshFailure(failureKind);
      return false;
    } finally {
      if (isCurrent(invitationId)) setPendingAction(null);
    }
  }

  async function settleMutationFailure(
    mutationFailure: ApiFailure,
    currentInvitation: InvitationResponse,
  ) {
    const terminal = terminalInvitationDecision(
      mutationFailure,
      currentInvitation,
    );
    if (terminal) {
      setDecision(terminal);
      setFailure(
        terminal.state === "recipient-mismatch" ? null : mutationFailure,
      );
      setRefreshFailure(null);
      return;
    }
    if (isInvitationNotPendingFailure(mutationFailure)) {
      setDecision((current) => ({ ...current, canRespond: false }));
      setFailure(mutationFailure);
      setRefreshFailure(null);
      await refreshDecision(currentInvitation.id, "not-pending");
      return;
    }
    setFailure(mutationFailure);
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
    setPendingAction(null);
    if (!result.ok) {
      mutationInFlight.current = false;
      await settleMutationFailure(result.failure, invitation);
      return;
    }
    if (result.data.invitationId !== invitationId) {
      setFailure({ kind: "network", code: "api_unavailable" });
      mutationInFlight.current = false;
      return;
    }
    setDecision({
      invitation: {
        ...invitation,
        status: "accepted",
        displayState: "accepted",
      },
      state: "accepted",
      canRespond: false,
    });
    setRefreshFailure(null);
    safelyRunOrQueue(invitationId, () => {
      if (!lifecycle.isCurrent(invitationId)) return;
      try {
        router.replace(
          organizationRoutes.dashboard(result.data.canonicalOrganizationKey),
        );
      } catch {
        // The committed terminal projection remains authoritative when client
        // navigation cannot start; no transport or router detail is exposed.
      }
    });
  }

  async function reject() {
    if (!actionable || !invitation || mutationInFlight.current) return;
    const invitationId = invitation.id;
    mutationInFlight.current = true;
    setPendingAction("reject");
    setFailure(null);
    setRefreshFailure(null);
    const result = await rejectBrowserInvitation(
      createBrowserApiClient(),
      invitationId,
    );
    if (!isCurrent(invitationId)) return;
    setPendingAction(null);
    if (!result.ok) {
      mutationInFlight.current = false;
      await settleMutationFailure(result.failure, invitation);
      return;
    }
    if (
      result.data.state !== "rejected" ||
      (result.data.invitation !== null &&
        result.data.invitation.id !== invitationId)
    ) {
      setFailure({ kind: "network", code: "api_unavailable" });
      mutationInFlight.current = false;
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
    await refreshDecision(invitationId, "saved");
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
    await refreshDecision(invitationId, "reconcile");
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

        <DecisionFailure
          failure={failure}
          messageCoveredByState={
            failure !== null &&
            failureIsRepresentedByDecision(failure, decision)
          }
        />
        {refreshFailure ? (
          <Alert variant="destructive">
            <AlertTitle>
              {refreshFailure === "saved"
                ? t("success.refreshFailure")
                : t("success.reconciliationFailure")}
            </AlertTitle>
            <AlertDescription>
              <Button
                disabled={pendingAction !== null}
                onClick={() =>
                  invitation &&
                  void refreshDecision(invitation.id, refreshFailure)
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
