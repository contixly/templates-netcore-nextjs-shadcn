"use client";

import type { Route } from "next";
import { useTranslations } from "next-intl";
import {
  IconBrandGithub,
  IconBrandGitlab,
  IconBrandGoogle,
  IconBrandVk,
  IconBrandYandex,
  IconLogin2,
  type Icon,
} from "@tabler/icons-react";
import { useId, useRef, useState } from "react";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/features/application/ui/interaction-readiness";
import { Alert, AlertDescription } from "@/src/components/ui/alert";
import { Button } from "@/src/components/ui/button";
import { Field, FieldGroup } from "@/src/components/ui/field";
import { startExternalAuth } from "@/src/lib/api/auth/browser/start-external-auth";
import type { AuthProviderResponse } from "@/src/lib/api/generated";
import type { ApiFailure } from "@/src/lib/api/result";

type ExternalProviderFailure =
  "failure" | "invalidAuthorizationUrl" | "unavailable";

const providerIcons: Readonly<Record<string, Icon>> = {
  github: IconBrandGithub,
  gitlab: IconBrandGitlab,
  google: IconBrandGoogle,
  vk: IconBrandVk,
  yandex: IconBrandYandex,
};

function failureKey(failure: ApiFailure): ExternalProviderFailure {
  return failure.kind === "network" || failure.kind === "configuration"
    ? "unavailable"
    : "failure";
}

function expectedAuthorizationUrl(value: string): string | undefined {
  try {
    const url = new URL(value);
    return url.protocol === "https:" &&
      url.hostname.length > 0 &&
      !url.username &&
      !url.password
      ? url.href
      : undefined;
  } catch {
    return undefined;
  }
}

export function ExternalProviderButtons({
  providers,
  returnUrl,
}: Readonly<{
  providers: readonly AuthProviderResponse[];
  returnUrl: Route;
}>) {
  const t = useTranslations("auth.externalProviders");
  const interactionReady = useInteractionReady();
  const headingId = useId();
  const inFlight = useRef(false);
  const [pendingProvider, setPendingProvider] = useState<
    AuthProviderResponse["id"] | null
  >(null);
  const [failure, setFailure] = useState<ExternalProviderFailure | null>(null);

  async function start(provider: AuthProviderResponse) {
    if (inFlight.current) {
      return;
    }

    inFlight.current = true;
    setPendingProvider(provider.id);
    setFailure(null);

    const result = await startExternalAuth({
      provider: provider.id,
      intent: "signIn",
      returnUrl,
    });
    if (!result.ok) {
      setFailure(failureKey(result.failure));
      setPendingProvider(null);
      inFlight.current = false;
      return;
    }

    const authorizationUrl = expectedAuthorizationUrl(
      result.data.authorizationUrl,
    );
    if (!authorizationUrl) {
      setFailure("invalidAuthorizationUrl");
      setPendingProvider(null);
      inFlight.current = false;
      return;
    }

    try {
      window.location.assign(authorizationUrl);
    } catch {
      setFailure("invalidAuthorizationUrl");
      setPendingProvider(null);
      inFlight.current = false;
    }
  }

  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-4">
      <div className="flex flex-col gap-1 text-center">
        <h2 className="text-sm font-medium" id={headingId}>
          {t("title")}
        </h2>
        <p className="text-xs text-muted-foreground">{t("description")}</p>
      </div>
      {failure ? (
        <Alert variant="destructive">
          <AlertDescription>{t(failure)}</AlertDescription>
        </Alert>
      ) : null}
      <FieldGroup>
        {providers.map((provider) => {
          const pending = pendingProvider === provider.id;
          const label = t("button", { provider: provider.displayName });
          const ProviderIcon = providerIcons[provider.id] ?? IconLogin2;

          return (
            <Field key={provider.id}>
              <Button
                {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
                aria-label={
                  pending
                    ? t("pending", { provider: provider.displayName })
                    : label
                }
                className="w-full"
                disabled={!interactionReady || pendingProvider !== null}
                onClick={() => void start(provider)}
                type="button"
                variant="outline"
              >
                <ProviderIcon aria-hidden="true" data-icon="inline-start" />
                {pending
                  ? t("pending", { provider: provider.displayName })
                  : label}
              </Button>
            </Field>
          );
        })}
      </FieldGroup>
    </section>
  );
}
