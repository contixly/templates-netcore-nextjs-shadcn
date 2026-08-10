"use client";

import { useSyncExternalStore } from "react";

export const ORGANIZATION_CONTROL_INTERACTION_READY_ATTRIBUTE =
  "data-organization-control-interaction-ready";

const subscribe = () => () => undefined;
const clientSnapshot = () => true;
const serverSnapshot = () => false;

export function useOrganizationControlInteractionReady() {
  return useSyncExternalStore(subscribe, clientSnapshot, serverSnapshot);
}
