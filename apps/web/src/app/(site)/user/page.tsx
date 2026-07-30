import { redirect } from "next/navigation";

import { accountRoutes } from "@/src/features/account/account-routes";

export default function UserPage(): never {
  redirect(accountRoutes.profile);
}
