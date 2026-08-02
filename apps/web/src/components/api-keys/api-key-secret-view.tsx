"use client";

import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
} from "react";
import { useTranslations } from "next-intl";
import { IconCopy } from "@tabler/icons-react";

import {
  INTERACTION_READY_ATTRIBUTE,
  useInteractionReady,
} from "@/src/components/application/interaction-readiness";
import { Alert, AlertTitle } from "@/src/components/ui/alert";
import { Button } from "@/src/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/src/components/ui/dialog";

export type ApiKeySecretViewHandle = Readonly<{
  reveal: (credential: string) => void;
  clear: () => void;
}>;

export const ApiKeySecretView = forwardRef<ApiKeySecretViewHandle>(
  function ApiKeySecretView(_props, ref) {
    const t = useTranslations("apiKeys.secret");
    const interactionReady = useInteractionReady();
    const credentialRef = useRef("");
    const [credential, setCredential] = useState<string | null>(null);
    const [copyState, setCopyState] = useState<"idle" | "copied" | "failed">(
      "idle",
    );

    function clear() {
      credentialRef.current = "";
      setCredential(null);
      setCopyState("idle");
    }

    useImperativeHandle(
      ref,
      () => ({
        reveal(nextCredential) {
          credentialRef.current = nextCredential;
          setCredential(nextCredential);
          setCopyState("idle");
        },
        clear,
      }),
      [],
    );

    useEffect(
      () => () => {
        credentialRef.current = "";
      },
      [],
    );

    async function copyCredential() {
      if (!credentialRef.current) return;
      try {
        await navigator.clipboard.writeText(credentialRef.current);
        setCopyState("copied");
      } catch {
        setCopyState("failed");
      }
    }

    return (
      <Dialog
        open={credential !== null}
        onOpenChange={(open) => !open && clear()}
      >
        <DialogContent showCloseButton={false}>
          <DialogHeader>
            <DialogTitle>{t("title")}</DialogTitle>
            <DialogDescription>{t("warning")}</DialogDescription>
          </DialogHeader>
          {credential !== null ? (
            <div className="flex flex-col gap-3">
              <div className="flex flex-col gap-1">
                <span className="text-xs text-muted-foreground">
                  {t("label")}
                </span>
                <code
                  className="border bg-muted p-3 text-xs break-all"
                  tabIndex={0}
                >
                  {credential}
                </code>
              </div>
              {copyState !== "idle" ? (
                <Alert
                  variant={copyState === "failed" ? "destructive" : "default"}
                >
                  <AlertTitle>
                    {copyState === "copied" ? t("copied") : t("copyFailure")}
                  </AlertTitle>
                </Alert>
              ) : null}
            </div>
          ) : null}
          <DialogFooter>
            <Button
              {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
              disabled={!interactionReady}
              onClick={clear}
              type="button"
              variant="outline"
            >
              {t("close")}
            </Button>
            <Button
              {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
              disabled={!interactionReady || credential === null}
              onClick={() => void copyCredential()}
              type="button"
            >
              <IconCopy data-icon="inline-start" />
              {t("copy")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    );
  },
);
