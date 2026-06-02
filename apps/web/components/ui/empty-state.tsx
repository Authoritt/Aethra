import * as React from "react";
import { cn } from "@/lib/utils";

export interface EmptyStateProps extends React.HTMLAttributes<HTMLDivElement> {
  // ReactNode (no LucideIcon) — un Server Component no puede pasar funciones
  // a Client Components. Convención: `icon={<FolderKanban className="h-6 w-6" />}`.
  icon?: React.ReactNode;
  title: string;
  description?: string;
  action?: React.ReactNode;
}

const EmptyState = React.forwardRef<HTMLDivElement, EmptyStateProps>(
  ({ icon, title, description, action, className, ...props }, ref) => {
    return (
      <div
        ref={ref}
        className={cn(
          "flex flex-col items-center justify-center gap-3 rounded-lg border border-dashed bg-card/50 px-6 py-16 text-center",
          className,
        )}
        {...props}
      >
        {icon ? (
          <div className="flex h-12 w-12 items-center justify-center rounded-full bg-muted text-muted-foreground">
            {icon}
          </div>
        ) : null}
        <div className="flex flex-col gap-1">
          <h3 className="text-base font-semibold text-foreground">{title}</h3>
          {description ? (
            <p className="max-w-md text-sm text-muted-foreground">
              {description}
            </p>
          ) : null}
        </div>
        {action ? <div className="mt-2">{action}</div> : null}
      </div>
    );
  },
);
EmptyState.displayName = "EmptyState";

export { EmptyState };
