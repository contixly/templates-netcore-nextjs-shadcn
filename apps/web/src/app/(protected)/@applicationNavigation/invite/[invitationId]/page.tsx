import { ApplicationNavigationSlot } from "@/src/components/application/application-navigation-slot";
import { collaborationRoutes } from "@/src/features/collaboration/collaboration-routes";

export default async function InvitationApplicationNavigation({
  params,
}: Readonly<{ params: Promise<{ invitationId: string }> }>) {
  const { invitationId } = await params;

  return (
    <ApplicationNavigationSlot
      redirectPath={collaborationRoutes.invitationDecision(invitationId)}
    />
  );
}
