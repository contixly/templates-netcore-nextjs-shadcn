import { ImageResponse } from "next/og";

import type { DocumentInfo } from "@/src/features/documents/documents-types";

export const DOCUMENT_SOCIAL_IMAGE_SIZE = {
  width: 1200,
  height: 630,
} as const;

const DOCUMENT_SOCIAL_IMAGE_CHROME = {
  en: {
    header: "Template documentation",
    footer: "Documentation",
  },
  ru: {
    header: "Документация Template",
    footer: "Документация",
  },
} as const;

export function createDocumentSocialImage(
  document: Pick<DocumentInfo, "contentLocale" | "meta">,
): ImageResponse {
  const chrome = DOCUMENT_SOCIAL_IMAGE_CHROME[document.contentLocale];

  return new ImageResponse(
    <div
      style={{
        alignItems: "stretch",
        background: "#09090b",
        color: "#fafafa",
        display: "flex",
        flexDirection: "column",
        height: "100%",
        justifyContent: "space-between",
        padding: "72px 80px",
        width: "100%",
      }}
    >
      <div
        style={{
          color: "#a1a1aa",
          display: "flex",
          fontSize: 30,
          fontWeight: 600,
          letterSpacing: "0.08em",
          textTransform: "uppercase",
        }}
      >
        {chrome.header}
      </div>
      <div style={{ display: "flex", flexDirection: "column", gap: 28 }}>
        <div
          style={{
            display: "flex",
            fontSize: 68,
            fontWeight: 700,
            letterSpacing: "-0.04em",
            lineHeight: 1.05,
          }}
        >
          {document.meta.title}
        </div>
        <div
          style={{
            color: "#d4d4d8",
            display: "flex",
            fontSize: 30,
            lineHeight: 1.35,
          }}
        >
          {document.meta.description}
        </div>
      </div>
      <div
        style={{
          alignItems: "center",
          display: "flex",
          fontSize: 28,
          justifyContent: "space-between",
        }}
      >
        <span>Next.js + ASP.NET Core</span>
        <span style={{ color: "#a1a1aa" }}>{chrome.footer}</span>
      </div>
    </div>,
    DOCUMENT_SOCIAL_IMAGE_SIZE,
  );
}
