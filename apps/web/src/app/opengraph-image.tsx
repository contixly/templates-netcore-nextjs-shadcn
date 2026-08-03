import { ImageResponse } from "next/og";

import { loadI18nMessagesConfig } from "@/src/i18n/messages";

export const alt = "Template application foundation";
export const contentType = "image/png";
export const size = { width: 1200, height: 630 };

export default async function OpenGraphImage() {
  const { messages } = await loadI18nMessagesConfig();

  return new ImageResponse(
    <div
      style={{
        alignItems: "center",
        background: "#171717",
        color: "#ffffff",
        display: "flex",
        height: "100%",
        justifyContent: "center",
        padding: "72px",
        width: "100%",
      }}
    >
      <div
        style={{
          border: "2px solid #525252",
          display: "flex",
          flexDirection: "column",
          gap: "28px",
          padding: "64px",
          width: "100%",
        }}
      >
        <div style={{ display: "flex", fontSize: 34, fontWeight: 700 }}>
          {messages.common.brand}
        </div>
        <div
          style={{
            display: "flex",
            fontSize: 68,
            fontWeight: 700,
            letterSpacing: "-0.04em",
            lineHeight: 1.05,
          }}
        >
          {messages.application.landing.title}
        </div>
        <div style={{ color: "#d4d4d4", display: "flex", fontSize: 28 }}>
          ASP.NET Core 10 + Next.js
        </div>
      </div>
    </div>,
    size,
  );
}
