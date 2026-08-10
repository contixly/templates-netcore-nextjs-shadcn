"use client";

import { useLocale, useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/features/application/ui/interaction-readiness";
import { Badge } from "@/src/components/ui/badge";
import { Button } from "@/src/components/ui/button";
import { Input } from "@/src/components/ui/input";
import { Label } from "@/src/components/ui/label";
import { Separator } from "@/src/components/ui/separator";
import { updateBrowserAccountProfile } from "@/src/lib/api/account/browser/account-mutations";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { AccountResponse } from "@/src/lib/api/generated";
import type { ApiFailure } from "@/src/lib/api/result";

function formattedDate(value: string, locale: string): string {
  const date = new Date(value);
  return Number.isNaN(date.valueOf())
    ? value
    : new Intl.DateTimeFormat(locale, {
        dateStyle: "medium",
        timeZone: "UTC",
      }).format(date);
}

function initials(displayName: string): string {
  const value = displayName
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0])
    .join("")
    .toUpperCase();
  return value || "?";
}

function failureTrace(failure: ApiFailure): string | undefined {
  return failure.kind === "problem" ? failure.traceId : undefined;
}

export function ProfileForm({
  headingLevel = 2,
  initialAccount,
}: Readonly<{ headingLevel?: 2 | 3; initialAccount: AccountResponse }>) {
  const t = useTranslations("account.profile");
  const locale = useLocale();
  const interactionReady = useInteractionReady();
  const [account, setAccount] = useState(initialAccount);
  const [displayName, setDisplayName] = useState(initialAccount.displayName);
  const [validationMessage, setValidationMessage] = useState<string | null>(
    null,
  );
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [updated, setUpdated] = useState(false);
  const [pending, setPending] = useState(false);
  const SectionHeading = headingLevel === 3 ? "h3" : "h2";
  const primaryEmailProjection = account.verifiedEmails.find(
    (email) => email.isPrimary,
  );
  const verifiedEmails = [
    {
      email: account.primaryEmail,
      isPrimary: true,
      providers: primaryEmailProjection?.providers ?? [],
    },
    ...account.verifiedEmails.filter((email) => !email.isPrimary),
  ];

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const normalizedName = displayName.trim();
    const length = normalizedName.length;

    setUpdated(false);
    setFailure(null);
    if (length < 2) {
      setValidationMessage(t("validation.tooShort"));
      return;
    }
    if (length > 50) {
      setValidationMessage(t("validation.tooLong"));
      return;
    }

    setValidationMessage(null);
    setPending(true);
    const result = await updateBrowserAccountProfile(createBrowserApiClient(), {
      displayName: normalizedName,
    });
    setPending(false);

    if (!result.ok) {
      setFailure(result.failure);
      return;
    }

    setAccount(result.data);
    setDisplayName(result.data.displayName);
    setUpdated(true);
  }

  return (
    <div className="flex flex-col gap-8">
      <section
        className="flex flex-col gap-3"
        aria-labelledby="profile-avatar-heading"
      >
        <div className="flex flex-col gap-1">
          <SectionHeading
            className="text-sm font-semibold"
            id="profile-avatar-heading"
          >
            {t("avatar")}
          </SectionHeading>
          <p className="text-xs text-muted-foreground">
            {t("avatarDescription")}
          </p>
        </div>
        <div
          aria-label={account.imageUrl ? t("avatar") : t("avatarFallback")}
          className="relative flex size-20 items-center justify-center overflow-hidden rounded-full bg-muted text-xl font-semibold"
          role="img"
        >
          <span aria-hidden="true">{initials(account.displayName)}</span>
          {account.imageUrl ? (
            // The API accepts only HTTPS avatar URLs. Avoid sending a Referer to
            // the external provider while retaining reference-compatible avatars.
            // eslint-disable-next-line @next/next/no-img-element
            <img
              alt=""
              className="absolute inset-0 size-full object-cover"
              height={80}
              referrerPolicy="no-referrer"
              src={account.imageUrl}
              width={80}
            />
          ) : null}
        </div>
      </section>

      <Separator />

      <section
        className="flex flex-col gap-3"
        aria-labelledby="profile-name-heading"
      >
        <div className="flex flex-col gap-1">
          <SectionHeading
            className="text-sm font-semibold"
            id="profile-name-heading"
          >
            {t("displayName")}
          </SectionHeading>
          <p className="text-xs text-muted-foreground">
            {t("displayNameHint")}
          </p>
        </div>
        <form className="flex flex-col gap-3" noValidate onSubmit={submit}>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start">
            <div className="flex flex-1 flex-col gap-2">
              <Label htmlFor="account-display-name">{t("displayName")}</Label>
              <Input
                {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
                aria-describedby="account-display-name-message"
                aria-invalid={validationMessage ? true : undefined}
                autoComplete="name"
                disabled={!interactionReady || pending}
                id="account-display-name"
                maxLength={100}
                onChange={(event) => {
                  setDisplayName(event.currentTarget.value);
                  setValidationMessage(null);
                  setFailure(null);
                  setUpdated(false);
                }}
                value={displayName}
              />
              <p
                className={
                  validationMessage
                    ? "text-xs text-destructive"
                    : "text-xs text-muted-foreground"
                }
                id="account-display-name-message"
                role={validationMessage ? "alert" : undefined}
              >
                {validationMessage ?? t("displayNameHint")}
              </p>
            </div>
            <Button
              {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
              className="sm:mt-5"
              disabled={!interactionReady || pending}
              type="submit"
            >
              {pending ? t("saving") : t("save")}
            </Button>
          </div>
          {failure ? (
            <div
              className="flex flex-col gap-1 text-sm text-destructive"
              role="alert"
            >
              <p>{t("updateFailure")}</p>
              {failureTrace(failure) ? (
                <p className="font-mono text-xs">{failureTrace(failure)}</p>
              ) : null}
            </div>
          ) : null}
          {updated ? (
            <p className="text-sm" role="status">
              {t("updated")}
            </p>
          ) : null}
        </form>
      </section>

      <Separator />

      <section
        className="flex flex-col gap-3"
        aria-labelledby="profile-emails-heading"
      >
        <SectionHeading
          className="text-sm font-semibold"
          id="profile-emails-heading"
        >
          {t("emails")}
        </SectionHeading>
        <dl className="divide-y border">
          {verifiedEmails.map((verifiedEmail) => (
            <div
              className="flex flex-col gap-1 px-3 py-3 sm:flex-row sm:items-center sm:justify-between"
              key={verifiedEmail.email}
            >
              <div className="min-w-0">
                <dt className="sr-only">
                  {verifiedEmail.isPrimary ? t("primary") : t("secondary")}
                </dt>
                <dd className="text-sm break-all">{verifiedEmail.email}</dd>
                {verifiedEmail.providers.length > 0 ? (
                  <dd className="text-xs text-muted-foreground">
                    {t("verifiedBy", {
                      providers: verifiedEmail.providers.join(", "),
                    })}
                  </dd>
                ) : null}
              </div>
              <Badge variant={verifiedEmail.isPrimary ? "default" : "outline"}>
                {verifiedEmail.isPrimary ? t("primary") : t("secondary")}
              </Badge>
            </div>
          ))}
        </dl>
      </section>

      <Separator />

      <dl className="grid gap-5 sm:grid-cols-2">
        <div className="flex flex-col gap-1">
          <dt className="text-xs font-medium text-muted-foreground">
            {t("userId")}
          </dt>
          <dd className="font-mono text-sm break-all">{account.id}</dd>
        </div>
        <div className="flex flex-col gap-1">
          <dt className="text-xs font-medium text-muted-foreground">
            {t("memberSince")}
          </dt>
          <dd className="text-sm">
            <time dateTime={account.createdAt}>
              {formattedDate(account.createdAt, locale)}
            </time>
          </dd>
        </div>
      </dl>
    </div>
  );
}
