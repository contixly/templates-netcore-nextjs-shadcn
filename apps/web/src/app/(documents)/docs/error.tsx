"use client";

export default function DocumentsError({
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  return (
    <section role="alert">
      <h2>Documentation is unavailable</h2>
      <p>The requested document could not be rendered safely.</p>
      <button type="button" onClick={reset}>
        Try again
      </button>
    </section>
  );
}
