"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useRef, useState, type FormEvent } from "react";
import { IconPlus } from "@tabler/icons-react";

import { Button } from "@/src/components/ui/button";
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
import { organizationRoutes } from "@/src/features/organizations/organization-routes";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import { createBrowserOrganization } from "@/src/lib/api/organizations/browser/organization-mutations";
import type { ApiFailure } from "@/src/lib/api/result";

const supportedOrganizationName = /^[\p{L}\p{N} _-]+$/u;

function traceId(failure: ApiFailure | null): string | undefined {
  return failure?.kind === "problem" ? failure.traceId : undefined;
}

export function OrganizationCreateDialog({
  presentation = "default",
}: Readonly<{ presentation?: "default" | "onboarding" }>) {
  const t = useTranslations("organizations.createDialog");
  const onboarding = useTranslations("organizations.onboarding");
  const router = useRouter();
  const requestInFlight = useRef(false);
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [validation, setValidation] = useState<string | null>(null);
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [pending, setPending] = useState(false);

  function changeOpen(nextOpen: boolean) {
    if (requestInFlight.current) {
      return;
    }
    setOpen(nextOpen);
    if (!nextOpen) {
      setName("");
      setValidation(null);
      setFailure(null);
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (requestInFlight.current) {
      return;
    }

    const normalizedName = name.trim();
    if (normalizedName.length === 0) {
      setValidation(t("validation.required"));
      return;
    }
    if (normalizedName.length > 50) {
      setValidation(t("validation.tooLong"));
      return;
    }
    if (!supportedOrganizationName.test(normalizedName)) {
      setValidation(t("validation.invalidCharacters"));
      return;
    }

    setValidation(null);
    setFailure(null);
    requestInFlight.current = true;
    setPending(true);
    const result = await createBrowserOrganization(createBrowserApiClient(), {
      name: normalizedName,
    });
    requestInFlight.current = false;
    setPending(false);

    if (!result.ok) {
      setFailure(result.failure);
      return;
    }

    setOpen(false);
    router.push(organizationRoutes.dashboard(result.data.canonicalKey));
    router.refresh();
  }

  const failureMessage =
    failure?.kind === "problem" && failure.code === "organization_name_conflict"
      ? t("nameConflict")
      : t("failure");

  return (
    <Dialog open={open} onOpenChange={changeOpen}>
      <DialogTrigger asChild>
        {presentation === "onboarding" ? (
          <Button size="lg" type="button">
            <IconPlus data-icon="inline-start" />
            {onboarding("createAction")}
          </Button>
        ) : (
          <Button type="button">{t("trigger")}</Button>
        )}
      </DialogTrigger>
      <DialogContent
        onEscapeKeyDown={(event) => {
          if (pending) {
            event.preventDefault();
          }
        }}
        onInteractOutside={(event) => {
          if (pending) {
            event.preventDefault();
          }
        }}
      >
        <DialogHeader>
          <DialogTitle>{t("title")}</DialogTitle>
          <DialogDescription>{t("description")}</DialogDescription>
        </DialogHeader>
        <form noValidate onSubmit={submit}>
          <FieldGroup>
            <Field data-invalid={validation ? true : undefined}>
              <FieldLabel htmlFor="organization-name">
                {t("nameLabel")}
              </FieldLabel>
              <Input
                aria-describedby="organization-name-description organization-name-error"
                aria-invalid={validation ? true : undefined}
                autoComplete="organization"
                autoFocus
                disabled={pending}
                id="organization-name"
                maxLength={100}
                onChange={(event) => {
                  setName(event.currentTarget.value);
                  setValidation(null);
                  setFailure(null);
                }}
                placeholder={t("namePlaceholder")}
                value={name}
              />
              <FieldDescription id="organization-name-description">
                {t("nameHint")}
              </FieldDescription>
              <FieldError id="organization-name-error">{validation}</FieldError>
            </Field>
            {failure ? (
              <div className="flex flex-col gap-1" role="alert">
                <p>{failureMessage}</p>
                {traceId(failure) ? (
                  <p className="font-mono text-xs text-muted-foreground">
                    {traceId(failure)}
                  </p>
                ) : null}
              </div>
            ) : null}
            <DialogFooter>
              <Button
                disabled={pending}
                onClick={() => changeOpen(false)}
                type="button"
                variant="outline"
              >
                {t("cancel")}
              </Button>
              <Button disabled={pending} type="submit">
                {pending ? t("pending") : t("submit")}
              </Button>
            </DialogFooter>
          </FieldGroup>
        </form>
      </DialogContent>
    </Dialog>
  );
}
