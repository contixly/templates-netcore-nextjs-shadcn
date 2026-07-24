"use client";

export default function GlobalError({
  reset,
}: Readonly<{
  error: Error & { digest?: string };
  reset: () => void;
}>) {
  return (
    <html lang="en">
      <body>
        <main
          style={{
            fontFamily: "system-ui, sans-serif",
            margin: "4rem auto",
            maxWidth: "40rem",
            padding: "0 1rem",
          }}
        >
          <h1>Application error</h1>
          <p>The application could not render safely.</p>
          <button onClick={reset} type="button">
            Reload application
          </button>
        </main>
      </body>
    </html>
  );
}
