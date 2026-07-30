"use client";

import { useSyncExternalStore } from "react";

export const INTERACTION_READY_ATTRIBUTE = "data-interaction-ready";

const subscribe = () => () => undefined;
const clientSnapshot = () => true;
const serverSnapshot = () => false;

export function useInteractionReady() {
  return useSyncExternalStore(subscribe, clientSnapshot, serverSnapshot);
}
