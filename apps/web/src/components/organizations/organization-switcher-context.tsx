"use client";

import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";

import {
  OrganizationSwitcher,
  type OrganizationSwitcherItem,
} from "@/src/components/organizations/organization-switcher";

export type OrganizationSwitcherContextValue = Readonly<{
  activeOrganizationId?: string | null;
  currentOrganization?: OrganizationSwitcherItem | null;
  nextCursor?: string | null;
  organizations: readonly OrganizationSwitcherItem[];
}>;

type OrganizationSwitcherContextState = Readonly<{
  context: OrganizationSwitcherContextValue | null;
  setContext: (context: OrganizationSwitcherContextValue | null) => void;
}>;

const OrganizationSwitcherContext =
  createContext<OrganizationSwitcherContextState | null>(null);

export function OrganizationSwitcherProvider({
  children,
}: Readonly<{ children: ReactNode }>) {
  const [context, setContext] =
    useState<OrganizationSwitcherContextValue | null>(null);
  const value = useMemo(() => ({ context, setContext }), [context]);

  return (
    <OrganizationSwitcherContext.Provider value={value}>
      {children}
    </OrganizationSwitcherContext.Provider>
  );
}

export function OrganizationSwitcherRegistration(
  context: OrganizationSwitcherContextValue,
) {
  const state = useContext(OrganizationSwitcherContext);
  const setContext = state?.setContext;

  useEffect(() => {
    setContext?.(context);
    return () => setContext?.(null);
  }, [context, setContext]);

  return null;
}

export function OrganizationSwitcherSlot() {
  const state = useContext(OrganizationSwitcherContext);

  return state?.context ? <OrganizationSwitcher {...state.context} /> : null;
}
