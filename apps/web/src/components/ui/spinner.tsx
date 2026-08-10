import { IconLoader2 } from "@tabler/icons-react";

import { cn } from "@/src/lib/utils";

export function Spinner({ className, ...props }: React.ComponentProps<"svg">) {
  return (
    <IconLoader2
      aria-hidden="true"
      className={cn("size-4 animate-spin", className)}
      {...props}
    />
  );
}
