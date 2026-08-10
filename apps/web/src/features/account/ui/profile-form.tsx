"use client";

import { useLocale, useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/features/application/ui/interaction-readiness";
import {
  Avatar,
  AvatarFallback,
  AvatarImage,
} from "@/src/components/ui/avatar";
import { Badge } from "@/src/components/ui/badge";
import { LoadingButton } from "@/src/components/ui/custom/button-loading";
import { FormErrorNotice } from "@/src/components/ui/custom/form-error-notice";
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@/src/components/ui/field";
import { Input } from "@/src/components/ui/input";
import {
  Item,
  ItemActions,
  ItemContent,
  ItemDescription,
  ItemGroup,
  ItemTitle,
} from "@/src/components/ui/item";
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
        <Avatar
          aria-label={account.imageUrl ? t("avatar") : t("avatarFallback")}
          className="size-20"
          role="img"
        >
          {account.imageUrl ? (
            // The API accepts only HTTPS avatar URLs. Avoid sending a Referer to
            // the external provider while retaining reference-compatible avatars.
            <AvatarImage
              alt=""
              referrerPolicy="no-referrer"
              src={account.imageUrl}
            />
          ) : null}
          <AvatarFallback className="text-lg">
            {initials(account.displayName)}
          </AvatarFallback>
        </Avatar>
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
        <form noValidate onSubmit={submit}>
          <FieldGroup>
            <Field
              className="items-start sm:flex-row"
              data-disabled={!interactionReady || pending}
              data-invalid={Boolean(validationMessage)}
            >
              <div className="flex flex-1 flex-col gap-2">
                <FieldLabel htmlFor="account-display-name">
                  {t("displayName")}
                </FieldLabel>
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
                {validationMessage ? (
                  <FieldError id="account-display-name-message">
                    {validationMessage}
                  </FieldError>
                ) : (
                  <FieldDescription id="account-display-name-message">
                    {t("displayNameHint")}
                  </FieldDescription>
                )}
              </div>
              <LoadingButton
                {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
                className="min-w-fit sm:mt-5"
                disabled={!interactionReady || pending}
                loading={pending}
                type="submit"
              >
                {pending ? t("saving") : t("save")}
              </LoadingButton>
            </Field>
            {failure ? (
              <FormErrorNotice title={t("updateFailure")}>
                {failureTrace(failure) ? (
                  <p className="font-mono text-xs">{failureTrace(failure)}</p>
                ) : null}
              </FormErrorNotice>
            ) : null}
            {updated ? (
              <p className="text-sm" role="status">
                {t("updated")}
              </p>
            ) : null}
          </FieldGroup>
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
        <ItemGroup>
          {verifiedEmails.map((verifiedEmail) => (
            <Item
              className="rounded-lg px-4 py-4 text-sm"
              key={verifiedEmail.email}
              variant="outline"
            >
              <ItemContent className="min-w-0">
                <ItemTitle className="text-sm break-all">
                  {verifiedEmail.email}
                </ItemTitle>
                {verifiedEmail.providers.length > 0 ? (
                  <ItemDescription>
                    {t("verifiedBy", {
                      providers: verifiedEmail.providers.join(", "),
                    })}
                  </ItemDescription>
                ) : null}
              </ItemContent>
              <ItemActions className="ml-auto">
                <Badge
                  variant={verifiedEmail.isPrimary ? "default" : "outline"}
                >
                  {verifiedEmail.isPrimary ? t("primary") : t("secondary")}
                </Badge>
              </ItemActions>
            </Item>
          ))}
        </ItemGroup>
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
