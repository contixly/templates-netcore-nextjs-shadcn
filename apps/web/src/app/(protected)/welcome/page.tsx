import { redirect } from "next/navigation";
import { connection } from "next/server";

import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { OrganizationOnboarding } from "@/src/components/organizations/organization-onboarding";
import { applicationRoutes } from "@/src/features/application/application-routes";
import { loadProtectedSession } from "@/src/features/authentication/load-protected-session";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { loadAccountInvitations } from "@/src/lib/api/collaboration/server/load-account-invitations";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";
import { buildApplicationPageMetadata } from "@/src/lib/metadata";

export function generateMetadata() {
  return buildApplicationPageMetadata("welcome");
}

export default async function WelcomePage() {
  await connection();
  const [session, organizations] = await Promise.all([
    loadProtectedSession(organizationRoutes.welcome),
    loadOrganizations(),
  ]);

  if (!session.ok) {
    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-12">
        <OrganizationFailure failure={session.failure} />
      </div>
    );
  }
  if (
    session.data.authenticated !== true ||
    !session.data.session ||
    !session.data.user
  ) {
    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-12">
        <OrganizationFailure
          failure={{ kind: "network", code: "api_unavailable" }}
        />
      </div>
    );
  }
  if (!organizations.ok) {
    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-12">
        <OrganizationFailure failure={organizations.failure} />
      </div>
    );
  }
  if (organizations.data.items.length > 0) {
    redirect(applicationRoutes.dashboard);
  }

  const invitations = await loadAccountInvitations({ limit: 20 });
  return (
    <OrganizationOnboarding
      initialInvitations={invitations.ok ? invitations.data : undefined}
    />
  );
}
