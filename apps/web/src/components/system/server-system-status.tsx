import { connection } from "next/server";

import { StatusCard } from "@/src/components/system/status-card";
import { loadServerSystemStatus } from "@/src/lib/api/server/load-server-system-status";

export async function ServerSystemStatus() {
  await connection();
  const result = await loadServerSystemStatus();

  return (
    <StatusCard
      source="ssr"
      state={
        result.ok
          ? { kind: "success", data: result.data }
          : { kind: "failure", failure: result.failure }
      }
    />
  );
}
