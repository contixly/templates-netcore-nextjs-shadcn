type LandingFooterProps = Readonly<{
  description: string;
  text: string;
}>;

export function LandingFooter({ description, text }: LandingFooterProps) {
  return (
    <footer className="px-4 py-8 sm:px-6 lg:px-8">
      <div className="mx-auto flex max-w-5xl flex-col gap-2 text-xs text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
        <span>{text}</span>
        <span>{description}</span>
      </div>
    </footer>
  );
}
