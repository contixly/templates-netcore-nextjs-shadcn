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
} from "@/src/features/application/ui/interaction-readiness";
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
    const mounted = useRef(true);
    const revealGeneration = useRef(0);
    const copyInFlight = useRef(false);
    const credentialRef = useRef("");
    const [credential, setCredential] = useState<string | null>(null);
    const [copyState, setCopyState] = useState<"idle" | "copied" | "failed">(
      "idle",
    );

    function clear() {
      revealGeneration.current += 1;
      copyInFlight.current = false;
      credentialRef.current = "";
      setCredential(null);
      setCopyState("idle");
    }

    useImperativeHandle(
      ref,
      () => ({
        reveal(nextCredential) {
          revealGeneration.current += 1;
          copyInFlight.current = false;
          credentialRef.current = nextCredential;
          setCredential(nextCredential);
          setCopyState("idle");
        },
        clear,
      }),
      [],
    );

    useEffect(() => {
      mounted.current = true;
      return () => {
        mounted.current = false;
        revealGeneration.current += 1;
        copyInFlight.current = false;
        credentialRef.current = "";
      };
    }, []);

    async function copyCredential() {
      const value = credentialRef.current;
      if (!value || copyInFlight.current) return;
      const generation = revealGeneration.current;
      copyInFlight.current = true;
      try {
        await navigator.clipboard.writeText(value);
        if (
          !mounted.current ||
          generation !== revealGeneration.current ||
          credentialRef.current !== value
        ) {
          return;
        }
        copyInFlight.current = false;
        setCopyState("copied");
      } catch {
        if (
          !mounted.current ||
          generation !== revealGeneration.current ||
          credentialRef.current !== value
        ) {
          return;
        }
        copyInFlight.current = false;
        setCopyState("failed");
      }
    }

    return (
      <Dialog
        open={credential !== null}
        onOpenChange={(open) => !open && clear()}
      >
        <DialogContent className="sm:max-w-xl" showCloseButton={false}>
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
                  className="max-h-32 overflow-y-auto rounded-none border bg-muted p-3 text-xs break-all select-all"
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
              disabled={!interactionReady || credential === null}
              onClick={() => void copyCredential()}
              type="button"
              variant="outline"
            >
              <IconCopy data-icon="inline-start" />
              {t("copy")}
            </Button>
            <Button
              {...{ [INTERACTION_READY_ATTRIBUTE]: interactionReady }}
              disabled={!interactionReady}
              onClick={clear}
              type="button"
            >
              {t("close")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    );
  },
);
