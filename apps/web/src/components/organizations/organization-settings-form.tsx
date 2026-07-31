"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useRef, useState, type FormEvent } from "react";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/components/application/interaction-readiness";
import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import { Button } from "@/src/components/ui/button";
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@/src/components/ui/field";
import { Input } from "@/src/components/ui/input";
import { Textarea } from "@/src/components/ui/textarea";
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type {
  OrganizationDetailResponse,
  UpdateOrganizationRequest,
} from "@/src/lib/api/generated/types.gen";
import { updateBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import type { ApiFailure } from "@/src/lib/api/result";

const supportedOrganizationName = /^[\p{L}\p{Nd} _-]+$/u;
const supportedOrganizationSlug = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const uuidShapedOrganizationSlug =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const supportedEmailDomain =
  /^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/;

export type OrganizationSettingsView = Readonly<{
  id: string;
  name: string;
  slug: string;
  canonicalKey: string;
  allowedEmailDomains: readonly string[];
  capabilities: Readonly<{ canUpdateOrganization: boolean }>;
}>;

type SettingsValidation = Readonly<{
  field: "domains" | "name" | "slug";
  message: string;
}>;

function parseDomains(value: string): {
  domains: string[];
  invalidDomain?: string;
} {
  const values = value
    .split(/[,\n]/)
    .map((domain) => domain.trim().toLowerCase().replace(/^@/, ""))
    .filter(Boolean);
  return {
    domains: [...new Set(values)],
    invalidDomain: values.find((domain) => !supportedEmailDomain.test(domain)),
  };
}

function toSettingsView(
  organization: OrganizationDetailResponse,
): OrganizationSettingsView {
  return {
    id: organization.id,
    name: organization.name,
    slug: organization.slug,
    canonicalKey: organization.canonicalKey,
    allowedEmailDomains: organization.allowedEmailDomains,
    capabilities: {
      canUpdateOrganization: organization.capabilities.canUpdateOrganization,
    },
  };
}

function failureTrace(failure: ApiFailure | null): string | undefined {
  return failure?.kind === "problem" ? failure.traceId : undefined;
}

function sameDomains(
  current: readonly string[],
  baseline: readonly string[],
): boolean {
  if (current.length !== baseline.length) {
    return false;
  }
  const baselineDomains = new Set(baseline);
  return current.every((domain) => baselineDomains.has(domain));
}

function dirtyUpdateRequest(
  baseline: OrganizationSettingsView,
  normalizedName: string,
  normalizedSlug: string,
  normalizedDomains: string[],
): UpdateOrganizationRequest | null {
  const nameChanged = normalizedName !== baseline.name;
  const slugChanged = normalizedSlug !== baseline.slug;
  const domainsChanged = !sameDomains(
    normalizedDomains,
    baseline.allowedEmailDomains,
  );

  if (nameChanged) {
    if (slugChanged) {
      return domainsChanged
        ? {
            name: normalizedName,
            slug: normalizedSlug,
            allowedEmailDomains: normalizedDomains,
          }
        : { name: normalizedName, slug: normalizedSlug };
    }
    return domainsChanged
      ? { name: normalizedName, allowedEmailDomains: normalizedDomains }
      : { name: normalizedName };
  }
  if (slugChanged) {
    return domainsChanged
      ? { slug: normalizedSlug, allowedEmailDomains: normalizedDomains }
      : { slug: normalizedSlug };
  }
  return domainsChanged ? { allowedEmailDomains: normalizedDomains } : null;
}

export function OrganizationSettingsForm({
  initialOrganization,
}: Readonly<{ initialOrganization: OrganizationSettingsView }>) {
  const t = useTranslations("organizations.settings.form");
  const router = useRouter();
  const interactionReady = useInteractionReady();
  const requestInFlight = useRef(false);
  const [organization, setOrganization] = useState(initialOrganization);
  const [name, setName] = useState(initialOrganization.name);
  const [slug, setSlug] = useState(initialOrganization.slug);
  const [domainsText, setDomainsText] = useState(
    initialOrganization.allowedEmailDomains.join("\n"),
  );
  const [validation, setValidation] = useState<SettingsValidation | null>(null);
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [success, setSuccess] = useState(false);
  const [pending, setPending] = useState(false);
  const canUpdate = initialOrganization.capabilities.canUpdateOrganization;
  const parsedDomains = parseDomains(domainsText);
  const previewDomains = parsedDomains.domains;
  const normalizedName = name.trim();
  const normalizedSlug = slug.trim().toLowerCase();
  const updateRequest = dirtyUpdateRequest(
    organization,
    normalizedName,
    normalizedSlug,
    previewDomains,
  );

  function resetFeedback() {
    setValidation(null);
    setFailure(null);
    setSuccess(false);
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canUpdate || requestInFlight.current) {
      return;
    }

    if (!normalizedName) {
      setValidation({
        field: "name",
        message: t("validation.nameRequired"),
      });
      return;
    }
    if (
      normalizedName.length > 50 ||
      !supportedOrganizationName.test(normalizedName)
    ) {
      setValidation({
        field: "name",
        message: t("validation.nameInvalid"),
      });
      return;
    }
    if (
      normalizedSlug.length > 64 ||
      !supportedOrganizationSlug.test(normalizedSlug) ||
      uuidShapedOrganizationSlug.test(normalizedSlug)
    ) {
      setValidation({
        field: "slug",
        message: t("validation.slugInvalid"),
      });
      return;
    }
    if (parsedDomains.invalidDomain) {
      setValidation({
        field: "domains",
        message: t("validation.domainsInvalid"),
      });
      return;
    }
    if (!updateRequest) {
      return;
    }

    requestInFlight.current = true;
    setPending(true);
    setValidation(null);
    setFailure(null);
    setSuccess(false);
    const result = await updateBrowserOrganization(
      createBrowserApiClient(),
      organization.id,
      updateRequest,
    );
    requestInFlight.current = false;
    setPending(false);

    if (!result.ok) {
      setFailure(result.failure);
      return;
    }

    const previousCanonicalKey = organization.canonicalKey;
    setOrganization(toSettingsView(result.data));
    setName(result.data.name);
    setSlug(result.data.slug);
    setDomainsText(result.data.allowedEmailDomains.join("\n"));
    setSuccess(true);

    if (result.data.canonicalKey !== previousCanonicalKey) {
      router.replace(
        organizationRoutes.settingsWorkspace(result.data.canonicalKey),
      );
    }
    router.refresh();
  }

  const failureMessage =
    failure?.kind === "problem"
      ? ({
          organization_name_conflict: t("failures.nameConflict"),
          organization_slug_conflict: t("failures.slugConflict"),
          organization_permission_denied: t("failures.permissionDenied"),
          validation_failed: t("failures.validation"),
          concurrency_conflict: t("failures.concurrency"),
        }[failure.code] ?? t("failures.generic"))
      : t("failures.generic");

  return (
    <div className="flex flex-col gap-4">
      {!canUpdate ? (
        <Alert>
          <AlertTitle>{t("readOnlyTitle")}</AlertTitle>
          <AlertDescription>{t("readOnlyDescription")}</AlertDescription>
        </Alert>
      ) : null}
      <form className="flex flex-col gap-5" noValidate onSubmit={submit}>
        <FieldGroup>
          <Field
            data-disabled={!canUpdate || pending ? true : undefined}
            data-invalid={validation?.field === "name" ? true : undefined}
          >
            <FieldLabel htmlFor="organization-settings-name">
              {t("nameLabel")}
            </FieldLabel>
            <Input
              {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
              aria-describedby={`organization-settings-name-hint${
                validation?.field === "name"
                  ? " organization-settings-name-error"
                  : ""
              }`}
              aria-invalid={validation?.field === "name" ? true : undefined}
              autoComplete="organization"
              disabled={!interactionReady || !canUpdate || pending}
              id="organization-settings-name"
              maxLength={100}
              onChange={(event) => {
                setName(event.currentTarget.value);
                resetFeedback();
              }}
              value={name}
            />
            <FieldDescription id="organization-settings-name-hint">
              {t("nameHint")}
            </FieldDescription>
            {validation?.field === "name" ? (
              <FieldError id="organization-settings-name-error">
                {validation.message}
              </FieldError>
            ) : null}
          </Field>
          <Field
            data-disabled={!canUpdate || pending ? true : undefined}
            data-invalid={validation?.field === "slug" ? true : undefined}
          >
            <FieldLabel htmlFor="organization-settings-slug">
              {t("slugLabel")}
            </FieldLabel>
            <Input
              {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
              aria-describedby={`organization-settings-slug-hint${
                validation?.field === "slug"
                  ? " organization-settings-slug-error"
                  : ""
              }`}
              aria-invalid={validation?.field === "slug" ? true : undefined}
              autoComplete="off"
              disabled={!interactionReady || !canUpdate || pending}
              id="organization-settings-slug"
              maxLength={128}
              onChange={(event) => {
                setSlug(event.currentTarget.value);
                resetFeedback();
              }}
              value={slug}
            />
            <FieldDescription id="organization-settings-slug-hint">
              {t("slugHint")}
            </FieldDescription>
            {validation?.field === "slug" ? (
              <FieldError id="organization-settings-slug-error">
                {validation.message}
              </FieldError>
            ) : null}
          </Field>
          <Field
            data-disabled={!canUpdate || pending ? true : undefined}
            data-invalid={validation?.field === "domains" ? true : undefined}
          >
            <FieldLabel htmlFor="organization-settings-domains">
              {t("domainsLabel")}
            </FieldLabel>
            <Textarea
              {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
              aria-describedby={`organization-settings-domains-hint${
                validation?.field === "domains"
                  ? " organization-settings-domains-error"
                  : ""
              }`}
              aria-invalid={validation?.field === "domains" ? true : undefined}
              autoComplete="off"
              disabled={!interactionReady || !canUpdate || pending}
              id="organization-settings-domains"
              onChange={(event) => {
                setDomainsText(event.currentTarget.value);
                resetFeedback();
              }}
              rows={4}
              value={domainsText}
            />
            <FieldDescription id="organization-settings-domains-hint">
              {t("domainsHint")}
            </FieldDescription>
            {domainsText.trim() ? (
              <FieldDescription aria-live="polite">
                {t("domainsPreview", {
                  domains: previewDomains.join(", ") || t("domainsNone"),
                })}
              </FieldDescription>
            ) : null}
            {validation?.field === "domains" ? (
              <FieldError id="organization-settings-domains-error">
                {validation.message}
              </FieldError>
            ) : null}
          </Field>
        </FieldGroup>

        {failure ? (
          <Alert variant="destructive">
            <AlertTitle>{failureMessage}</AlertTitle>
            {failureTrace(failure) ? (
              <AlertDescription className="font-mono text-xs">
                {failureTrace(failure)}
              </AlertDescription>
            ) : null}
          </Alert>
        ) : null}
        {success ? (
          <p className="text-sm" role="status">
            {t("success")}
          </p>
        ) : null}
        {canUpdate ? (
          <div className="flex justify-end">
            <Button
              {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
              disabled={!interactionReady || pending || !updateRequest}
              type="submit"
            >
              {pending ? t("saving") : t("save")}
            </Button>
          </div>
        ) : null}
      </form>
    </div>
  );
}
