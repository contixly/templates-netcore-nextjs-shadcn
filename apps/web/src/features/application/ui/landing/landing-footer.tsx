type LandingFooterProps = Readonly<{
  description: string;
  text: string;
}>;

export function LandingFooter({ description, text }: LandingFooterProps) {
  return (
    <footer className="border-t px-4 py-8 md:px-6">
      <div className="mx-auto flex max-w-5xl flex-col items-center justify-between gap-4 text-xs text-muted-foreground md:flex-row">
        <span>{text}</span>
        <span>{description}</span>
      </div>
    </footer>
  );
}
