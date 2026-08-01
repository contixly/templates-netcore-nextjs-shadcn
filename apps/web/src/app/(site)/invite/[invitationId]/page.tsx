import { redirect } from "next/navigation";
import { connection } from "next/server";

import { InvitationDecision } from "@/src/components/collaboration/invitation-decision";
import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { authLoginUrl } from "@/src/features/authentication/sanitize-auth-redirect";
import { collaborationRoutes } from "@/src/features/collaboration/collaboration-routes";
import { recipientMismatchDecision } from "@/src/features/collaboration/invitation-decision-failure";
import { loadServerAuthState } from "@/src/lib/api/auth/server/load-server-auth-state";
import { loadInvitationDecision } from "@/src/lib/api/collaboration/server/load-invitation-decision";

type InvitationDecisionPageProps = Readonly<{
  params: Promise<{ invitationId: string }>;
}>;

export default async function InvitationDecisionPage({
  params,
}: InvitationDecisionPageProps) {
  await connection();
  const { invitationId } = await params;
  const route = collaborationRoutes.invitationDecision(invitationId);
  const auth = await loadServerAuthState();
  if (!auth.ok) return <OrganizationFailure failure={auth.failure} />;
  if (auth.data.session.authenticated === false) {
    redirect(authLoginUrl(route));
  }
  if (!auth.data.session.user || !auth.data.session.session) {
    return (
      <OrganizationFailure
        failure={{ kind: "network", code: "api_unavailable" }}
      />
    );
  }

  const decision = await loadInvitationDecision(invitationId);
  if (!decision.ok) {
    const mismatch = recipientMismatchDecision(decision.failure);
    if (!mismatch) return <OrganizationFailure failure={decision.failure} />;
    return (
      <main className="mx-auto flex w-full max-w-2xl flex-1 px-4 py-12">
        <InvitationDecision
          key={invitationId}
          decision={mismatch}
          emailVerified={auth.data.session.user.emailVerified}
          localEmailConfirmationAvailable={false}
        />
      </main>
    );
  }

  return (
    <main className="mx-auto flex w-full max-w-2xl flex-1 px-4 py-12">
      <InvitationDecision
        key={invitationId}
        decision={decision.data}
        emailVerified={auth.data.session.user.emailVerified}
        localEmailConfirmationAvailable={
          auth.data.capabilities.localAutomationEnabled
        }
      />
    </main>
  );
}
