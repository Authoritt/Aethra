"use client";

import { Card, CardContent } from "@/components/ui/card";
import { MetricsChart } from "@/components/aethra/metrics-chart";
import type { MonitorCheckDto } from "@/lib/types";

interface Props {
  checks: MonitorCheckDto[];
}

/**
 * Sparkline de latencia. Recibe checks en orden cronológico ascendente.
 */
export default function MonitorLatencyChart({ checks }: Props) {
  const data = checks
    .filter((c) => c.latency_ms !== null)
    .map((c) => ({
      timestamp: c.timestamp,
      latency: c.latency_ms ?? 0,
    }));

  if (data.length === 0) {
    return (
      <Card>
        <CardContent className="flex h-56 items-center justify-center text-sm text-muted-foreground">
          Sin checks todavía. Lanzá uno manual o esperá al próximo intervalo.
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardContent className="p-3">
        <MetricsChart
          data={data}
          series={[{ dataKey: "latency", label: "Latencia", tone: "info" }]}
          variant="line"
          formatValue={(v) => `${v}ms`}
          height={224}
        />
      </CardContent>
    </Card>
  );
}
