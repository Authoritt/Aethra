import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "@/lib/utils";

const statusPillVariants = cva(
  "inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-xs font-medium transition-colors",
  {
    variants: {
      variant: {
        success:
          "border-success/20 bg-success/10 text-success-foreground [&_.status-dot]:bg-success",
        warning:
          "border-warning/20 bg-warning/10 text-warning-foreground [&_.status-dot]:bg-warning",
        destructive:
          "border-destructive/20 bg-destructive/10 text-destructive-foreground [&_.status-dot]:bg-destructive",
        info: "border-info/20 bg-info/10 text-info-foreground [&_.status-dot]:bg-info",
        muted:
          "border-border bg-muted text-muted-foreground [&_.status-dot]:bg-muted-foreground",
        running:
          "border-info/20 bg-info/10 text-info-foreground [&_.status-dot]:bg-info",
      },
    },
    defaultVariants: {
      variant: "muted",
    },
  },
);

export type StatusPillVariant = NonNullable<
  VariantProps<typeof statusPillVariants>["variant"]
>;

export interface StatusPillProps
  extends React.HTMLAttributes<HTMLSpanElement>,
    VariantProps<typeof statusPillVariants> {
  withDot?: boolean;
  label?: React.ReactNode;
}

const StatusPill = React.forwardRef<HTMLSpanElement, StatusPillProps>(
  (
    { className, variant = "muted", withDot = true, label, children, ...props },
    ref,
  ) => {
    const isPulsing = variant === "running";
    return (
      <span
        ref={ref}
        className={cn(statusPillVariants({ variant }), className)}
        {...props}
      >
        {withDot ? (
          <span className="relative inline-flex h-2 w-2">
            {isPulsing ? (
              <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-current opacity-60 status-dot" />
            ) : null}
            <span className="relative inline-flex h-2 w-2 rounded-full status-dot" />
          </span>
        ) : null}
        <span>{label ?? children}</span>
      </span>
    );
  },
);
StatusPill.displayName = "StatusPill";

const STATUS_MAP: Record<string, StatusPillVariant> = {
  // Build/deploy lifecycle
  completed: "success",
  succeeded: "success",
  success: "success",
  ok: "success",
  active: "success",
  online: "success",
  healthy: "success",
  issued: "success",
  connected: "success",
  ready: "success",

  failed: "destructive",
  error: "destructive",
  errored: "destructive",
  unhealthy: "destructive",
  expired: "destructive",
  revoked: "destructive",
  disconnected: "destructive",
  cancelled: "destructive",
  canceled: "destructive",

  pending: "warning",
  queued: "warning",
  waiting: "warning",
  renewing: "warning",
  degraded: "warning",

  running: "running",
  building: "running",
  pulling: "running",
  deploying: "running",
  healthcheck: "running",
  swapping: "running",
  starting: "running",
  provisioning: "running",
  restarting: "running",

  unknown: "muted",
  stopped: "muted",
  idle: "muted",
  draft: "muted",
  disabled: "muted",
};

export function getStatusVariant(status: string | null | undefined): StatusPillVariant {
  if (!status) return "muted";
  const key = status.trim().toLowerCase();
  return STATUS_MAP[key] ?? "info";
}

export { StatusPill, statusPillVariants };
