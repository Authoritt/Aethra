"use client";

import * as React from "react";
import {
  Area,
  AreaChart,
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { cn } from "@/lib/utils";

export type MetricsChartVariant = "area" | "line";

export interface MetricsChartPoint {
  timestamp: string | number;
  [key: string]: string | number;
}

export interface MetricsChartSeries {
  dataKey: string;
  label: string;
  /** Token semántico: `primary`, `info`, `success`, `warning`, `destructive`. */
  tone?: "primary" | "info" | "success" | "warning" | "destructive";
}

export interface MetricsChartProps {
  data: MetricsChartPoint[];
  series: MetricsChartSeries[];
  variant?: MetricsChartVariant;
  /** Etiqueta del eje Y (opcional). */
  yLabel?: string;
  /** Función para formatear valores en tooltip + eje Y. */
  formatValue?: (v: number) => string;
  /** Función para formatear el tick del eje X. Por default formatea ISO -> HH:mm. */
  formatX?: (v: string | number) => string;
  className?: string;
  height?: number;
}

const TONE_STROKE: Record<NonNullable<MetricsChartSeries["tone"]>, string> = {
  primary: "hsl(var(--primary))",
  info: "hsl(var(--info))",
  success: "hsl(var(--success))",
  warning: "hsl(var(--warning))",
  destructive: "hsl(var(--destructive))",
};

/**
 * Chart re-estilizado con tokens del DS (theme-aware light/dark/branded).
 * Soporta multi-serie en variantes area o line.
 */
export function MetricsChart({
  data,
  series,
  variant = "area",
  yLabel,
  formatValue,
  formatX = defaultFormatX,
  className,
  height = 240,
}: MetricsChartProps) {
  const Chart = variant === "line" ? LineChart : AreaChart;
  return (
    <div className={cn("w-full", className)} style={{ height }}>
      <ResponsiveContainer width="100%" height="100%">
        <Chart data={data} margin={{ top: 8, right: 8, bottom: 0, left: yLabel ? 32 : 8 }}>
          <defs>
            {series.map((s) => {
              const stroke = TONE_STROKE[s.tone ?? "primary"];
              return (
                <linearGradient id={`grad-${s.dataKey}`} key={s.dataKey} x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor={stroke} stopOpacity={0.3} />
                  <stop offset="100%" stopColor={stroke} stopOpacity={0} />
                </linearGradient>
              );
            })}
          </defs>
          <CartesianGrid stroke="hsl(var(--border))" strokeDasharray="3 3" vertical={false} />
          <XAxis
            dataKey="timestamp"
            stroke="hsl(var(--muted-foreground))"
            fontSize={11}
            tickLine={false}
            axisLine={false}
            tickFormatter={formatX}
            minTickGap={32}
          />
          <YAxis
            stroke="hsl(var(--muted-foreground))"
            fontSize={11}
            tickLine={false}
            axisLine={false}
            width={40}
            tickFormatter={(v) => (formatValue ? formatValue(Number(v)) : String(v))}
            label={
              yLabel
                ? {
                    value: yLabel,
                    angle: -90,
                    position: "insideLeft",
                    offset: 8,
                    style: { fill: "hsl(var(--muted-foreground))", fontSize: 11 },
                  }
                : undefined
            }
          />
          <Tooltip
            contentStyle={{
              background: "hsl(var(--popover))",
              border: "1px solid hsl(var(--border))",
              borderRadius: "var(--radius, 0.5rem)",
              color: "hsl(var(--popover-foreground))",
              fontSize: 12,
            }}
            labelFormatter={(label) => formatX(label as string | number)}
            formatter={(v) =>
              formatValue ? formatValue(Number(v)) : String(v)
            }
          />
          {series.map((s) => {
            const stroke = TONE_STROKE[s.tone ?? "primary"];
            if (variant === "line") {
              return (
                <Line
                  key={s.dataKey}
                  type="monotone"
                  dataKey={s.dataKey}
                  name={s.label}
                  stroke={stroke}
                  strokeWidth={2}
                  dot={false}
                  isAnimationActive={false}
                />
              );
            }
            return (
              <Area
                key={s.dataKey}
                type="monotone"
                dataKey={s.dataKey}
                name={s.label}
                stroke={stroke}
                strokeWidth={2}
                fill={`url(#grad-${s.dataKey})`}
                isAnimationActive={false}
              />
            );
          })}
        </Chart>
      </ResponsiveContainer>
    </div>
  );
}

function defaultFormatX(v: string | number): string {
  try {
    const d = new Date(v);
    return d.toLocaleTimeString("es-ES", { hour: "2-digit", minute: "2-digit", hour12: false });
  } catch {
    return String(v);
  }
}
