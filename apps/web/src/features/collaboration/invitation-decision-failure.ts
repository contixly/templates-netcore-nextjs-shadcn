import type {
  InvitationDecisionResponse,
  InvitationResponse,
} from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

export function sanitizeInvitationDecision(
  decision: InvitationDecisionResponse,
): InvitationDecisionResponse {
  switch (decision.state) {
    case "recipient-mismatch":
      return {
        invitation: null,
        state: "recipient-mismatch",
        canRespond: false,
      };
    default:
      return decision;
  }
}

export function recipientMismatchDecision(
  failure: ApiFailure,
): InvitationDecisionResponse | null {
  if (failure.kind !== "problem") return null;
  switch (failure.code) {
    case "invitation_recipient_mismatch":
      return {
        invitation: null,
        state: "recipient-mismatch",
        canRespond: false,
      };
    default:
      return null;
  }
}

export function terminalInvitationDecision(
  failure: ApiFailure,
  invitation: InvitationResponse,
): InvitationDecisionResponse | null {
  const mismatch = recipientMismatchDecision(failure);
  if (mismatch) return mismatch;
  if (failure.kind !== "problem") return null;

  switch (failure.code) {
    case "invitation_expired":
      return { invitation, state: "expired", canRespond: false };
    case "invitation_domain_restricted":
      return { invitation, state: "domain-restricted", canRespond: false };
    case "invitation_email_verification_required":
      return {
        invitation,
        state: "email-verification-required",
        canRespond: false,
      };
    case "invitation_recipient_already_member":
      return { invitation, state: "already-member", canRespond: false };
    default:
      return null;
  }
}

export function isInvitationNotPendingFailure(failure: ApiFailure): boolean {
  if (failure.kind !== "problem") return false;
  switch (failure.code) {
    case "invitation_not_pending":
      return true;
    default:
      return false;
  }
}

export function failureIsRepresentedByDecision(
  failure: ApiFailure,
  decision: InvitationDecisionResponse,
): boolean {
  if (failure.kind !== "problem") return false;
  switch (failure.code) {
    case "invitation_expired":
      return decision.state === "expired";
    case "invitation_domain_restricted":
      return decision.state === "domain-restricted";
    case "invitation_email_verification_required":
      return decision.state === "email-verification-required";
    case "invitation_recipient_already_member":
      return decision.state === "already-member";
    default:
      return false;
  }
}
