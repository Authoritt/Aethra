import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { formatBytes } from "@/lib/utils";
import type { DatabaseDiskUsageDto } from "@/lib/types";

/**
 * Panel de uso de disco de la base de datos central. Render presentacional (sin estado): recibe el
 * DTO ya ordenado desc por bytes desde GET /api/metrics/database y dibuja una barra relativa por tabla.
 * Da visibilidad de dónde crece el disco y permite verificar que la retención mantiene acotadas las
 * tablas de alto volumen (vm_metrics, monitor_checks, etc.).
 */
export function DiskUsageCard({ data }: { data: DatabaseDiskUsageDto }) {
  const tables = data.tables ?? [];
  const max = tables.length > 0 ? Math.max(...tables.map((t) => t.totalBytes)) : 0;

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between gap-4 space-y-0">
        <CardTitle className="text-base">Uso de disco · Base de datos</CardTitle>
        <span className="text-xs text-muted-foreground">
          {formatBytes(data.databaseSizeBytes)} total · {data.tableCount} tablas
        </span>
      </CardHeader>
      <CardContent className="space-y-2.5">
        {tables.length === 0 ? (
          <p className="text-sm text-muted-foreground">Sin datos de uso de disco.</p>
        ) : (
          tables.map((t) => {
            const pct = max > 0 ? Math.max(2, (t.totalBytes / max) * 100) : 0;
            const name =
              t.schema && t.schema !== "public" ? `${t.schema}.${t.table}` : t.table;
            return (
              <div key={`${t.schema}.${t.table}`} className="space-y-1">
                <div className="flex items-center justify-between gap-3 text-xs">
                  <span className="truncate font-mono" title={`${t.schema}.${t.table}`}>
                    {name}
                  </span>
                  <span className="shrink-0 text-muted-foreground tabular-nums">
                    {formatBytes(t.totalBytes)}
                    <span className="ml-2">{formatRows(t.estimatedRows)} filas</span>
                  </span>
                </div>
                <div className="h-1.5 w-full overflow-hidden rounded-full bg-muted">
                  <div
                    className="h-full rounded-full bg-primary/70"
                    style={{ width: `${pct}%` }}
                  />
                </div>
              </div>
            );
          })
        )}
      </CardContent>
    </Card>
  );
}

/** Filas en forma compacta: 1.2M / 3.4k / 42. */
function formatRows(n: number): string {
  if (!Number.isFinite(n) || n <= 0) return "0";
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}k`;
  return String(n);
}
