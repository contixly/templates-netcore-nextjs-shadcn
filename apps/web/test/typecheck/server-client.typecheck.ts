import { createServerApiClient } from "@/src/lib/api/server/client";
import {
  createLocalAutomationScenario,
  signInLocalAutomation,
} from "@/src/lib/api/generated";
import type { Client } from "@/src/lib/api/generated/client";

declare const generatedClient: Client;

createServerApiClient();
createServerApiClient({ cookie: "__Host-template.session=value" });
createServerApiClient({ correlationId: "trace-123" });
createServerApiClient({
  cookie: "__Host-template.session=value",
  correlationId: "trace-123",
});
createLocalAutomationScenario({
  client: generatedClient,
  headers: { "X-CSRF-TOKEN": "csrf-token" },
});
// @ts-expect-error Credential sign-in must never advertise an optional body.
signInLocalAutomation({
  client: generatedClient,
  headers: { "X-CSRF-TOKEN": "csrf-token" },
});

// @ts-expect-error Arbitrary headers, including Authorization, are not accepted.
createServerApiClient({ authorization: "Bearer forbidden" });
