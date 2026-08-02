"use client";

import { useRef, useState, type RefObject } from "react";
import { useTranslations } from "next-intl";
import { IconPlus } from "@tabler/icons-react";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/components/application/interaction-readiness";
import {
  ApiKeySecretView,
  type ApiKeySecretViewHandle,
} from "@/src/components/api-keys/api-key-secret-view";
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
import type { ApiKeyOwner } from "@/src/features/api-keys/api-key-routes";
import {
  API_KEY_EXPIRY_OPTIONS,
  API_KEY_PRESET_OPTIONS,
  API_KEY_RATE_LIMIT_WINDOW_OPTIONS,
  PERSONAL_API_KEY_DEFAULTS,
  type ApiKeyExpiry,
  type ApiKeyPresetId,
  type ApiKeyRateLimitWindow,
} from "@/src/features/api-keys/api-key-options";
import { createBrowserApiKey } from "@/src/lib/api/api-keys/browser/api-key-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { ApiKeyResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiFailure } from "@/src/lib/api/result";

type Validation = Readonly<{
  name?: "nameRequired" | "nameTooLong" | "nameControl";
  presets?: "presetRequired";
  rateLimit?: "rateLimitRange";
}>;

function validate(
  name: string,
  presetIds: readonly ApiKeyPresetId[],
  rateLimitEnabled: boolean,
  rateLimitMax: number,
): Validation {
  const normalized = name.trim();
  return {
    ...(!normalized ? { name: "nameRequired" as const } : {}),
    ...([...normalized].length > 32 ? { name: "nameTooLong" as const } : {}),
    ...(/[\p{Cc}]/u.test(normalized) ? { name: "nameControl" as const } : {}),
    ...(presetIds.length === 0 ? { presets: "presetRequired" as const } : {}),
    ...(rateLimitEnabled &&
    (!Number.isInteger(rateLimitMax) ||
      rateLimitMax < 1 ||
      rateLimitMax > 1_000_000)
      ? { rateLimit: "rateLimitRange" as const }
      : {}),
  };
}

function failureCode(failure: ApiFailure): string {
  return failure.kind === "problem" ? failure.code : "generic";
}

function failureTrace(failure: ApiFailure): string | undefined {
  return failure.kind === "problem" ? failure.traceId : undefined;
}

export function ApiKeyCreateDialog({
  onConfirmed,
  owner,
  secretViewRef,
}: Readonly<{
  onConfirmed: (apiKey: ApiKeyResponse) => void;
  owner: ApiKeyOwner;
  secretViewRef?: RefObject<ApiKeySecretViewHandle | null>;
}>) {
  const t = useTranslations("apiKeys");
  const interactionReady = useInteractionReady();
  const localSecretView = useRef<ApiKeySecretViewHandle>(null);
  const secretView = secretViewRef ?? localSecretView;
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [presetIds, setPresetIds] = useState<ApiKeyPresetId[]>([
    ...PERSONAL_API_KEY_DEFAULTS.presetIds,
  ]);
  const [expiresIn, setExpiresIn] = useState<ApiKeyExpiry>(
    PERSONAL_API_KEY_DEFAULTS.expiresIn,
  );
  const [rateLimitEnabled, setRateLimitEnabled] = useState<boolean>(
    PERSONAL_API_KEY_DEFAULTS.rateLimitEnabled,
  );
  const [rateLimitMax, setRateLimitMax] = useState<number>(
    PERSONAL_API_KEY_DEFAULTS.rateLimitMax,
  );
  const [rateLimitWindow, setRateLimitWindow] = useState<ApiKeyRateLimitWindow>(
    PERSONAL_API_KEY_DEFAULTS.rateLimitWindow,
  );
  const [validation, setValidation] = useState<Validation>({});
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [pending, setPending] = useState(false);

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
    if (pending) return;
    const nextValidation = validate(
      name,
      presetIds,
      rateLimitEnabled,
      rateLimitMax,
    );
    setValidation(nextValidation);
    setFailure(null);
    if (Object.keys(nextValidation).length > 0) return;

    setPending(true);
    const result = await createBrowserApiKey(createBrowserApiClient(), owner, {
      name: name.trim(),
      presetIds,
      expiresIn,
      rateLimitEnabled,
      rateLimitMax,
      rateLimitWindow,
    });
    setPending(false);
    if (!result.ok) {
      setFailure(result.failure);
      return;
    }

    const { key, ...safeApiKey } = result.data;
    onConfirmed(safeApiKey);
    setOpen(false);
    secretView.current?.reveal(key);
  }

  const code = failure ? failureCode(failure) : null;
  const knownFailure =
    code === "antiforgery_failed" ||
    code === "api_key_not_found" ||
    code === "api_key_permission_denied" ||
    code === "api_key_update_unchanged" ||
    code === "validation_failed"
      ? code
      : "generic";

  return (
    <>
      <Dialog open={open} onOpenChange={(next) => !pending && setOpen(next)}>
        <DialogTrigger asChild>
          <Button
            {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
            disabled={!interactionReady}
            type="button"
          >
            <IconPlus data-icon="inline-start" />
            {t("actions.create")}
          </Button>
        </DialogTrigger>
        <DialogContent showCloseButton={false}>
          <DialogHeader>
            <DialogTitle>{t("create.title")}</DialogTitle>
            <DialogDescription>{t("create.description")}</DialogDescription>
          </DialogHeader>
          <form
            className="flex flex-col gap-5"
            onSubmit={(event) => void submit(event)}
          >
            <FieldGroup>
              <Field data-invalid={Boolean(validation.name)}>
                <FieldLabel htmlFor="api-key-create-name">
                  {t("fields.name")}
                </FieldLabel>
                <Input
                  aria-invalid={Boolean(validation.name)}
                  autoComplete="off"
                  id="api-key-create-name"
                  maxLength={64}
                  onChange={(event) => setName(event.target.value)}
                  value={name}
                />
                <FieldDescription>{t("fields.nameHint")}</FieldDescription>
                {validation.name ? (
                  <FieldError>{t(`validation.${validation.name}`)}</FieldError>
                ) : null}
              </Field>

              <fieldset
                className="flex flex-col gap-3"
                data-invalid={Boolean(validation.presets)}
              >
                <legend className="text-xs font-medium">
                  {t("presets.label")}
                </legend>
                <p className="text-xs text-muted-foreground">
                  {t("presets.description")}
                </p>
                <div className="grid gap-3 sm:grid-cols-2">
                  {API_KEY_PRESET_OPTIONS.map((option) => {
                    const id = `api-key-create-preset-${option.id}`;
                    return (
                      <Field key={option.id} orientation="horizontal">
                        <Checkbox
                          aria-invalid={Boolean(validation.presets)}
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
                {validation.presets ? (
                  <FieldError>
                    {t(`validation.${validation.presets}`)}
                  </FieldError>
                ) : null}
              </fieldset>

              <Field>
                <FieldLabel htmlFor="api-key-create-expiry">
                  {t("expiry.label")}
                </FieldLabel>
                <Select
                  value={expiresIn}
                  onValueChange={(value) => setExpiresIn(value as ApiKeyExpiry)}
                >
                  <SelectTrigger
                    aria-label={t("expiry.label")}
                    id="api-key-create-expiry"
                  >
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
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
                <FieldLabel htmlFor="api-key-create-rate-enabled">
                  {t("fields.rateLimitEnabled")}
                </FieldLabel>
                <Switch
                  checked={rateLimitEnabled}
                  id="api-key-create-rate-enabled"
                  onCheckedChange={setRateLimitEnabled}
                />
              </Field>

              <Field
                data-disabled={!rateLimitEnabled}
                data-invalid={Boolean(validation.rateLimit)}
              >
                <FieldLabel htmlFor="api-key-create-rate-max">
                  {t("fields.rateLimitMax")}
                </FieldLabel>
                <Input
                  aria-invalid={Boolean(validation.rateLimit)}
                  disabled={!rateLimitEnabled}
                  id="api-key-create-rate-max"
                  max={1_000_000}
                  min={1}
                  onChange={(event) =>
                    setRateLimitMax(event.target.valueAsNumber)
                  }
                  type="number"
                  value={Number.isNaN(rateLimitMax) ? "" : rateLimitMax}
                />
                {validation.rateLimit ? (
                  <FieldError>
                    {t(`validation.${validation.rateLimit}`)}
                  </FieldError>
                ) : null}
              </Field>

              <Field data-disabled={!rateLimitEnabled}>
                <FieldLabel htmlFor="api-key-create-rate-window">
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
                    id="api-key-create-rate-window"
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

            {failure ? (
              <Alert variant="destructive">
                <AlertTitle>{t("failures.create")}</AlertTitle>
                <AlertDescription>
                  <p>{t(`failures.codes.${knownFailure}`)}</p>
                  {failureTrace(failure) ? (
                    <p className="font-mono">{failureTrace(failure)}</p>
                  ) : null}
                </AlertDescription>
              </Alert>
            ) : null}

            <DialogFooter>
              <Button
                disabled={pending}
                onClick={() => setOpen(false)}
                type="button"
                variant="outline"
              >
                {t("actions.cancel")}
              </Button>
              <Button disabled={pending} type="submit">
                {pending ? t("create.submitting") : t("actions.create")}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
      {secretViewRef ? null : <ApiKeySecretView ref={localSecretView} />}
    </>
  );
}
