"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useRef, useState, type FormEvent } from "react";

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
import type { OrganizationDetailResponse } from "@/src/lib/api/generated/types.gen";
import { updateBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import type { ApiFailure } from "@/src/lib/api/result";

const supportedOrganizationName = /^[\p{L}\p{Nd} _-]+$/u;
const supportedOrganizationSlug = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

function normalizedDomains(value: string): string[] {
  const domains = value
    .split(/[,\n]/)
    .map((domain) => domain.trim().toLowerCase().replace(/^@/, ""))
    .filter(Boolean);
  return [...new Set(domains)];
}

function failureTrace(failure: ApiFailure | null): string | undefined {
  return failure?.kind === "problem" ? failure.traceId : undefined;
}

export function OrganizationSettingsForm({
  initialOrganization,
}: Readonly<{ initialOrganization: OrganizationDetailResponse }>) {
  const t = useTranslations("organizations.settings.form");
  const router = useRouter();
  const requestInFlight = useRef(false);
  const [organization, setOrganization] = useState(initialOrganization);
  const [name, setName] = useState(initialOrganization.name);
  const [slug, setSlug] = useState(initialOrganization.slug);
  const [domainsText, setDomainsText] = useState(
    initialOrganization.allowedEmailDomains.join("\n"),
  );
  const [validation, setValidation] = useState<string | null>(null);
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [success, setSuccess] = useState(false);
  const [pending, setPending] = useState(false);
  const canUpdate = organization.capabilities.canUpdateOrganization;
  const previewDomains = normalizedDomains(domainsText);

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

    const normalizedName = name.trim();
    const normalizedSlug = slug.trim().toLowerCase();
    if (!normalizedName) {
      setValidation(t("validation.nameRequired"));
      return;
    }
    if (
      normalizedName.length > 50 ||
      !supportedOrganizationName.test(normalizedName)
    ) {
      setValidation(t("validation.nameInvalid"));
      return;
    }
    if (
      normalizedSlug.length > 64 ||
      !supportedOrganizationSlug.test(normalizedSlug)
    ) {
      setValidation(t("validation.slugInvalid"));
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
      {
        name: normalizedName,
        slug: normalizedSlug,
        allowedEmailDomains: previewDomains,
      },
    );
    requestInFlight.current = false;
    setPending(false);

    if (!result.ok) {
      setFailure(result.failure);
      return;
    }

    const previousCanonicalKey = organization.canonicalKey;
    setOrganization(result.data);
    setName(result.data.name);
    setSlug(result.data.slug);
    setDomainsText(result.data.allowedEmailDomains.join("\n"));
    setSuccess(true);

    if (result.data.canonicalKey !== previousCanonicalKey) {
      router.replace(
        organizationRoutes.settingsWorkspace(result.data.canonicalKey),
      );
    }
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
            data-invalid={validation ? true : undefined}
          >
            <FieldLabel htmlFor="organization-settings-name">
              {t("nameLabel")}
            </FieldLabel>
            <Input
              aria-invalid={validation ? true : undefined}
              autoComplete="organization"
              disabled={!canUpdate || pending}
              id="organization-settings-name"
              maxLength={100}
              onChange={(event) => {
                setName(event.currentTarget.value);
                resetFeedback();
              }}
              value={name}
            />
            <FieldDescription>{t("nameHint")}</FieldDescription>
          </Field>
          <Field data-disabled={!canUpdate || pending ? true : undefined}>
            <FieldLabel htmlFor="organization-settings-slug">
              {t("slugLabel")}
            </FieldLabel>
            <Input
              autoComplete="off"
              disabled={!canUpdate || pending}
              id="organization-settings-slug"
              maxLength={128}
              onChange={(event) => {
                setSlug(event.currentTarget.value);
                resetFeedback();
              }}
              value={slug}
            />
            <FieldDescription>{t("slugHint")}</FieldDescription>
          </Field>
          <Field data-disabled={!canUpdate || pending ? true : undefined}>
            <FieldLabel htmlFor="organization-settings-domains">
              {t("domainsLabel")}
            </FieldLabel>
            <Textarea
              autoComplete="off"
              disabled={!canUpdate || pending}
              id="organization-settings-domains"
              onChange={(event) => {
                setDomainsText(event.currentTarget.value);
                resetFeedback();
              }}
              rows={4}
              value={domainsText}
            />
            <FieldDescription>{t("domainsHint")}</FieldDescription>
            {domainsText.trim() ? (
              <FieldDescription aria-live="polite">
                {t("domainsPreview", {
                  domains: previewDomains.join(", ") || t("domainsNone"),
                })}
              </FieldDescription>
            ) : null}
          </Field>
        </FieldGroup>

        {validation ? <FieldError>{validation}</FieldError> : null}
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
            <Button disabled={pending} type="submit">
              {pending ? t("saving") : t("save")}
            </Button>
          </div>
        ) : null}
      </form>
    </div>
  );
}
