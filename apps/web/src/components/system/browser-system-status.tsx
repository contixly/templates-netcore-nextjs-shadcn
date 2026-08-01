"use client";

import { useEffect, useState } from "react";

import { useInteractionReady } from "@/src/components/application/interaction-readiness";
import {
  StatusCard,
  type StatusCardState,
} from "@/src/components/system/status-card";
import { loadBrowserSystemStatus } from "@/src/lib/api/browser/load-browser-system-status";

export function BrowserSystemStatus() {
  const interactionReady = useInteractionReady();
  const [attempt, setAttempt] = useState(0);
  const [state, setState] = useState<StatusCardState>({ kind: "loading" });

  useEffect(() => {
    const controller = new AbortController();
    let active = true;

    void loadBrowserSystemStatus(controller.signal).then((result) => {
      if (!active || controller.signal.aborted) {
        return;
      }

      setState(
        result.ok
          ? { kind: "success", data: result.data }
          : { kind: "failure", failure: result.failure },
      );
    });

    return () => {
      active = false;
      controller.abort();
    };
  }, [attempt]);

  function retry() {
    setState({ kind: "loading" });
    setAttempt((value) => value + 1);
  }

  return (
    <StatusCard
      interactionReady={interactionReady}
      onRetry={retry}
      source="browser"
      state={state}
    />
  );
}
