import { redirect } from "next/navigation";
import { connection } from "next/server";

import { OrganizationFailure } from "@/src/components/organizations/organization-list";
import { OrganizationOnboarding } from "@/src/components/organizations/organization-onboarding";
import { applicationRoutes } from "@/src/features/application/application-routes";
import { loadOrganizations } from "@/src/lib/api/organizations/server/load-organizations";

export default async function WelcomePage() {
  await connection();
  const organizations = await loadOrganizations();

  if (!organizations.ok) {
    return (
      <main className="mx-auto w-full max-w-5xl px-4 py-12">
        <OrganizationFailure failure={organizations.failure} />
      </main>
    );
  }
  if (organizations.data.items.length > 0) {
    redirect(applicationRoutes.dashboard);
  }

  return <OrganizationOnboarding />;
}
