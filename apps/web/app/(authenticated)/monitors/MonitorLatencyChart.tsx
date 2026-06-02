"use client";

import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { MonitorCheckDto } from "@/lib/types";

interface Props {
  checks: MonitorCheckDto[];
}

/**
 * Sparkline de latencia. Recibe checks en orden cronológico ascendente y muestra
 * latencyMs en el eje Y. Los checks Down con latencia null se muestran como gaps.
 */
export default function MonitorLatencyChart({ checks }: Props) {
  const data = checks.map((c) => ({
    t: formatTime(c.timestamp),
    latency: c.latency_ms ?? null,
    status: c.status,
  }));

  if (data.length === 0) {
    return (
      <div className="flex h-56 items-center justify-center rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 text-sm text-zinc-500">
        Sin checks todavía. Lanza uno manual o espera al próximo intervalo.
      </div>
    );
  }

  return (
    <div className="h-56 w-full rounded-2xl border border-zinc-800 bg-zinc-900/40 p-3">
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={data} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
          <CartesianGrid stroke="#27272a" strokeDasharray="3 3" />
          <XAxis
            dataKey="t"
            stroke="#52525b"
            fontSize={11}
            tickLine={false}
            axisLine={{ stroke: "#3f3f46" }}
            minTickGap={32}
          />
          <YAxis
            stroke="#52525b"
            fontSize={11}
            tickLine={false}
            axisLine={{ stroke: "#3f3f46" }}
            tickFormatter={(v) => `${v}ms`}
            width={50}
          />
          <Tooltip
            contentStyle={{
              background: "#09090b",
              border: "1px solid #27272a",
              borderRadius: 8,
              fontSize: 12,
            }}
            labelStyle={{ color: "#a1a1aa" }}
            formatter={(value, _name, item) => {
              const status = item?.payload?.status ?? "Unknown";
              const lat =
                typeof value === "number" ? value : Number(value) || 0;
              return [`${lat}ms · ${status}`, "latencia"];
            }}
          />
          <Line
            type="monotone"
            dataKey="latency"
            stroke="#10b981"
            strokeWidth={2}
            dot={false}
            isAnimationActive={false}
            connectNulls={false}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

function formatTime(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  const hh = String(d.getHours()).padStart(2, "0");
  const mm = String(d.getMinutes()).padStart(2, "0");
  return `${hh}:${mm}`;
}
