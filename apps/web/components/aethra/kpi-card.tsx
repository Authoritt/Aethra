"use client";

import * as React from "react";
import { Area, AreaChart, ResponsiveContainer } from "recharts";
import { Card, CardContent } from "@/components/ui/card";
import { cn } from "@/lib/utils";

export interface KpiCardProps {
  label: string;
  value: React.ReactNode;
  delta?: React.ReactNode;
  // ReactNode (no LucideIcon) para que un Server Component pueda pasar
  // `icon={<FolderKanban className="h-4 w-4" />}` sin atravesar el boundary
  // server→client con una function (componentes lucide son functions).
  icon?: React.ReactNode;
  sparkline?: number[];
  /** Variante visual del énfasis del número. */
  tone?: "default" | "success" | "warning" | "destructive" | "info";
  className?: string;
}

const TONE: Record<NonNullable<KpiCardProps["tone"]>, string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  destructive: "text-destructive",
  info: "text-info",
};

const STROKE: Record<NonNullable<KpiCardProps["tone"]>, string> = {
  default: "hsl(var(--primary))",
  success: "hsl(var(--success))",
  warning: "hsl(var(--warning))",
  destructive: "hsl(var(--destructive))",
  info: "hsl(var(--info))",
};

export function KpiCard({
  label,
  value,
  delta,
  icon,
  sparkline,
  tone = "default",
  className,
}: KpiCardProps) {
  const stroke = STROKE[tone];
  const data = React.useMemo(
    () => (sparkline ?? []).map((y, i) => ({ x: i, y })),
    [sparkline],
  );

  return (
    <Card className={cn("relative overflow-hidden", className)}>
      <CardContent className="p-5">
        <div className="flex items-start justify-between gap-3">
          <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            {label}
          </p>
          {icon ? (
            <div className="flex h-7 w-7 items-center justify-center rounded-md bg-muted text-muted-foreground">
              {icon}
            </div>
          ) : null}
        </div>
        <div className="mt-3 flex items-baseline gap-2">
          <div
            className={cn(
              "text-3xl font-semibold tracking-tight tabular-nums",
              TONE[tone],
            )}
          >
            {value}
          </div>
          {delta ? (
            <span className="text-xs text-muted-foreground">{delta}</span>
          ) : null}
        </div>
        {sparkline && sparkline.length > 1 ? (
          <div className="mt-3 h-10">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={data} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
                <defs>
                  <linearGradient id={`spark-${tone}`} x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor={stroke} stopOpacity={0.35} />
                    <stop offset="100%" stopColor={stroke} stopOpacity={0} />
                  </linearGradient>
                </defs>
                <Area
                  type="monotone"
                  dataKey="y"
                  stroke={stroke}
                  strokeWidth={1.5}
                  fill={`url(#spark-${tone})`}
                  isAnimationActive={false}
                />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}
