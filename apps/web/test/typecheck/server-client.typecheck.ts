import { createServerApiClient } from "@/src/lib/api/server/client";

createServerApiClient();
createServerApiClient({ cookie: "__Host-template.session=value" });
createServerApiClient({ correlationId: "trace-123" });
createServerApiClient({
  cookie: "__Host-template.session=value",
  correlationId: "trace-123",
});

// @ts-expect-error Arbitrary headers, including Authorization, are not accepted.
createServerApiClient({ authorization: "Bearer forbidden" });
