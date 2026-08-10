"use client";

import { IconCheck, IconCopy } from "@tabler/icons-react";
import { useState } from "react";

import { Button } from "@/src/components/ui/button";

export function DocumentsCopyButton({
  href,
  label,
  successLabel,
  value,
}: Readonly<{
  href?: string;
  label: string;
  successLabel: string;
  value?: string;
}>) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    const copyValue =
      value ?? (href ? new URL(href, window.location.href).href : "");
    if (!copyValue || !navigator.clipboard) return;

    try {
      await navigator.clipboard.writeText(copyValue);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      setCopied(false);
    }
  }

  return (
    <Button
      aria-label={copied ? successLabel : label}
      onClick={copy}
      size="icon-xs"
      title={copied ? successLabel : label}
      type="button"
      variant="ghost"
    >
      {copied ? (
        <IconCheck aria-hidden="true" />
      ) : (
        <IconCopy aria-hidden="true" />
      )}
    </Button>
  );
}
