"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/features/application/ui/interaction-readiness";
import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import { Button } from "@/src/components/ui/button";
import { Checkbox } from "@/src/components/ui/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/src/components/ui/dialog";
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@/src/components/ui/field";
import { Input } from "@/src/components/ui/input";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/src/components/ui/select";
import { Switch } from "@/src/components/ui/switch";
import {
  apiKeyFailureMessage,
  apiKeyIdentityMismatchFailure,
  apiKeyMutationBusyFailure,
} from "@/src/features/api-keys/api-key-failures";
import type {
  ApiKeyMutationArbiter,
  ApiKeyMutationLease,
} from "@/src/features/api-keys/api-key-mutation-arbiter";
import type { ApiKeyOwner } from "@/src/features/api-keys/api-key-routes";
import {
  API_KEY_EXPIRY_OPTIONS,
  API_KEY_PRESET_OPTIONS,
  API_KEY_RATE_LIMIT_WINDOW_OPTIONS,
  apiKeyPresetIdsForScopes,
  apiKeyScopesForPresetIds,
  type ApiKeyExpiry,
  type ApiKeyPresetId,
  type ApiKeyRateLimitWindow,
} from "@/src/features/api-keys/api-key-options";
import { updateBrowserApiKey } from "@/src/lib/api/api-keys/browser/api-key-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type {
  ApiKeyResponse,
  UpdateApiKeyRequest,
} from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

function sameValues(left: readonly string[], right: readonly string[]) {
  return (
    left.length === right.length &&
    left.every((value, index) => value === right[index])
  );
}

function validRateLimitMax(value: number) {
  return Number.isInteger(value) && value >= 1 && value <= 1_000_000;
}

export function ApiKeyEditDialog({
  apiKey,
  mutationArbiter,
  mutationBusy = false,
  onConfirmed,
  owner,
}: Readonly<{
  apiKey: ApiKeyResponse;
  mutationArbiter?: ApiKeyMutationArbiter;
  mutationBusy?: boolean;
  onConfirmed: (apiKey: ApiKeyResponse) => void;
  owner: ApiKeyOwner;
}>) {
  const t = useTranslations("apiKeys");
  const interactionReady = useInteractionReady();
  const initialPresets = apiKeyPresetIdsForScopes(apiKey.scopes);
  const mounted = useRef(true);
  const actionGeneration = useRef(0);
  const requestInFlight = useRef(false);
  const [open, setOpen] = useState(false);
  const [name, setName] = useState(apiKey.name);
  const [presetIds, setPresetIds] = useState<ApiKeyPresetId[]>(initialPresets);
  const [expiresIn, setExpiresIn] = useState<"unchanged" | ApiKeyExpiry>(
    "unchanged",
  );
  const [enabled, setEnabled] = useState(apiKey.enabled);
  const [rateLimitEnabled, setRateLimitEnabled] = useState(
    apiKey.rateLimitEnabled,
  );
  const [rateLimitMax, setRateLimitMax] = useState(apiKey.rateLimitMax);
  const [rateLimitWindow, setRateLimitWindow] = useState<ApiKeyRateLimitWindow>(
    apiKey.rateLimitWindow,
  );
  const [validation, setValidation] = useState<string | null>(null);
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [pending, setPending] = useState(false);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      actionGeneration.current += 1;
      requestInFlight.current = false;
    };
  }, []);

  function resetForm() {
    setName(apiKey.name);
    setPresetIds(apiKeyPresetIdsForScopes(apiKey.scopes));
    setExpiresIn("unchanged");
    setEnabled(apiKey.enabled);
    setRateLimitEnabled(apiKey.rateLimitEnabled);
    setRateLimitMax(apiKey.rateLimitMax);
    setRateLimitWindow(apiKey.rateLimitWindow);
    setValidation(null);
    setFailure(null);
  }

  function changeOpen(nextOpen: boolean) {
    if (requestInFlight.current) return;
    if (nextOpen) resetForm();
    setOpen(nextOpen);
  }

  function togglePreset(id: ApiKeyPresetId, checked: boolean) {
    setPresetIds((current) =>
      checked
        ? current.includes(id)
          ? current
          : [...current, id]
        : current.filter((value) => value !== id),
    );
  }

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (requestInFlight.current) return;
    const normalizedName = name.trim();
    if (!normalizedName) return setValidation("nameRequired");
    if ([...normalizedName].length > 32) return setValidation("nameTooLong");
    if (/[\p{Cc}]/u.test(normalizedName)) return setValidation("nameControl");
    if (presetIds.length === 0) return setValidation("presetRequired");
    if (rateLimitEnabled && !validRateLimitMax(rateLimitMax))
      return setValidation("rateLimitRange");

    const body: UpdateApiKeyRequest = {};
    if (normalizedName !== apiKey.name) body.name = normalizedName;
    if (!sameValues(apiKeyScopesForPresetIds(presetIds), apiKey.scopes)) {
      body.presetIds = presetIds;
    }
    if (expiresIn !== "unchanged") body.expiresIn = expiresIn;
    if (enabled !== apiKey.enabled) body.enabled = enabled;
    if (rateLimitEnabled !== apiKey.rateLimitEnabled) {
      body.rateLimitEnabled = rateLimitEnabled;
    }
    if (
      validRateLimitMax(rateLimitMax) &&
      rateLimitMax !== apiKey.rateLimitMax
    ) {
      body.rateLimitMax = rateLimitMax;
    }
    if (rateLimitWindow !== apiKey.rateLimitWindow) {
      body.rateLimitWindow = rateLimitWindow;
    }
    if (Object.keys(body).length === 0) return setValidation("noChanges");

    const lease: ApiKeyMutationLease | undefined =
      mutationArbiter?.acquire(apiKey.id) ?? undefined;
    if (mutationArbiter && !lease) {
      setFailure(apiKeyMutationBusyFailure());
      return;
    }

    setValidation(null);
    setFailure(null);
    requestInFlight.current = true;
    const generation = ++actionGeneration.current;
    setPending(true);
    try {
      const result = await updateBrowserApiKey(
        createBrowserApiClient(),
        owner,
        apiKey.id,
        body,
      );
      if (
        !mounted.current ||
        generation !== actionGeneration.current ||
        (lease && !mutationArbiter?.isCurrent(lease))
      ) {
        return;
      }
      requestInFlight.current = false;
      setPending(false);
      if (!result.ok) return setFailure(result.failure);
      if (result.data.id !== apiKey.id) {
        setFailure(apiKeyIdentityMismatchFailure());
        return;
      }
      setOpen(false);
      onConfirmed(result.data);
    } finally {
      if (lease) mutationArbiter?.release(lease);
    }
  }

  const failureCode = failure ? apiKeyFailureMessage(failure) : "generic";

  return (
    <Dialog open={open} onOpenChange={changeOpen}>
      <DialogTrigger asChild>
        <Button
          {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
          disabled={!interactionReady || mutationBusy}
          size="sm"
          type="button"
          variant="outline"
        >
          {t("actions.edit")}
        </Button>
      </DialogTrigger>
      <DialogContent showCloseButton={false}>
        <DialogHeader>
          <DialogTitle>{t("edit.title", { name: apiKey.name })}</DialogTitle>
          <DialogDescription>{t("edit.description")}</DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-5"
          onSubmit={(event) => void submit(event)}
        >
          <FieldGroup>
            <Field data-invalid={validation?.startsWith("name")}>
              <FieldLabel htmlFor={`api-key-edit-name-${apiKey.id}`}>
                {t("fields.name")}
              </FieldLabel>
              <Input
                aria-invalid={validation?.startsWith("name")}
                id={`api-key-edit-name-${apiKey.id}`}
                onChange={(event) => setName(event.target.value)}
                value={name}
              />
              <FieldDescription>{t("fields.nameHint")}</FieldDescription>
            </Field>
            <fieldset className="flex flex-col gap-3">
              <legend className="text-xs font-medium">
                {t("presets.label")}
              </legend>
              <div className="grid gap-3 sm:grid-cols-2">
                {API_KEY_PRESET_OPTIONS.map((option) => {
                  const id = `api-key-edit-${apiKey.id}-${option.id}`;
                  return (
                    <Field key={option.id} orientation="horizontal">
                      <Checkbox
                        checked={presetIds.includes(option.id)}
                        id={id}
                        onCheckedChange={(checked) =>
                          togglePreset(option.id, checked === true)
                        }
                      />
                      <FieldLabel htmlFor={id}>
                        {t(`presets.${option.id}`)}
                      </FieldLabel>
                    </Field>
                  );
                })}
              </div>
            </fieldset>
            <Field>
              <FieldLabel htmlFor={`api-key-edit-expiry-${apiKey.id}`}>
                {t("expiry.label")}
              </FieldLabel>
              <Select
                value={expiresIn}
                onValueChange={(value) =>
                  setExpiresIn(value as "unchanged" | ApiKeyExpiry)
                }
              >
                <SelectTrigger
                  aria-label={t("expiry.label")}
                  id={`api-key-edit-expiry-${apiKey.id}`}
                >
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value="unchanged">
                      {t("expiry.unchanged")}
                    </SelectItem>
                    {API_KEY_EXPIRY_OPTIONS.map((option) => (
                      <SelectItem key={option} value={option}>
                        {t(`expiry.${option}`)}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
            <Field orientation="horizontal">
              <FieldLabel htmlFor={`api-key-edit-enabled-${apiKey.id}`}>
                {t("fields.enabled")}
              </FieldLabel>
              <Switch
                id={`api-key-edit-enabled-${apiKey.id}`}
                checked={enabled}
                onCheckedChange={setEnabled}
              />
            </Field>
            <Field orientation="horizontal">
              <FieldLabel htmlFor={`api-key-edit-rate-enabled-${apiKey.id}`}>
                {t("fields.rateLimitEnabled")}
              </FieldLabel>
              <Switch
                id={`api-key-edit-rate-enabled-${apiKey.id}`}
                checked={rateLimitEnabled}
                onCheckedChange={(checked) => {
                  if (!checked && !validRateLimitMax(rateLimitMax)) {
                    setRateLimitMax(apiKey.rateLimitMax);
                  }
                  setRateLimitEnabled(checked);
                }}
              />
            </Field>
            <Field data-disabled={!rateLimitEnabled}>
              <FieldLabel htmlFor={`api-key-edit-rate-max-${apiKey.id}`}>
                {t("fields.rateLimitMax")}
              </FieldLabel>
              <Input
                disabled={!rateLimitEnabled}
                id={`api-key-edit-rate-max-${apiKey.id}`}
                max={1_000_000}
                min={1}
                onChange={(event) =>
                  setRateLimitMax(event.target.valueAsNumber)
                }
                type="number"
                value={Number.isNaN(rateLimitMax) ? "" : rateLimitMax}
              />
            </Field>
            <Field data-disabled={!rateLimitEnabled}>
              <FieldLabel htmlFor={`api-key-edit-rate-window-${apiKey.id}`}>
                {t("rateWindow.label")}
              </FieldLabel>
              <Select
                disabled={!rateLimitEnabled}
                value={rateLimitWindow}
                onValueChange={(value) =>
                  setRateLimitWindow(value as ApiKeyRateLimitWindow)
                }
              >
                <SelectTrigger
                  aria-label={t("rateWindow.label")}
                  id={`api-key-edit-rate-window-${apiKey.id}`}
                >
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    {API_KEY_RATE_LIMIT_WINDOW_OPTIONS.map((option) => (
                      <SelectItem key={option} value={option}>
                        {t(`rateWindow.${option}`)}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
          </FieldGroup>

          {validation ? (
            <FieldError>
              {validation === "noChanges"
                ? t("edit.noChanges")
                : t(`validation.${validation as "nameRequired"}`)}
            </FieldError>
          ) : null}
          {failure ? (
            <Alert variant="destructive">
              <AlertTitle>{t("failures.update")}</AlertTitle>
              <AlertDescription>
                <p>{t(`failures.codes.${failureCode}`)}</p>
                {failure.kind === "problem" && failure.traceId ? (
                  <p className="font-mono">{failure.traceId}</p>
                ) : null}
              </AlertDescription>
            </Alert>
          ) : null}
          <DialogFooter>
            <Button
              disabled={pending}
              onClick={() => changeOpen(false)}
              type="button"
              variant="outline"
            >
              {t("actions.cancel")}
            </Button>
            <Button disabled={pending || mutationBusy} type="submit">
              {pending ? t("edit.submitting") : t("actions.save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
