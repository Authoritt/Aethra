import { useTranslations } from "next-intl";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { cn, formatBytes } from "@/lib/utils";

export interface NodeDisk {
  name: string;
  slug: string;
  totalBytes: number | null;
  availableBytes: number | null;
}

/**
 * Panel "Disco por nodo": barra de uso de disco raíz por VM del clúster con GB libres + % libre.
 * Sirve para ver de un vistazo dónde hay espacio para distribuir (backups via satellite://, etc.).
 * Render presentacional — recibe los nodos ya proyectados desde /api/ops/machines.
 */
export function NodeDiskCard({ nodes }: { nodes: NodeDisk[] }) {
  const tr = useTranslations("components.node_disk");
  const withDisk = nodes.filter((n) => (n.totalBytes ?? 0) > 0);
  const totalFree = withDisk.reduce((s, n) => s + (n.availableBytes ?? 0), 0);

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between gap-4 space-y-0">
        <CardTitle className="text-base">{tr("title")}</CardTitle>
        <span className="text-xs text-muted-foreground">
          {tr("free_total", { size: formatBytes(totalFree) })}
        </span>
      </CardHeader>
      <CardContent className="space-y-3">
        {withDisk.length === 0 ? (
          <p className="text-sm text-muted-foreground">{tr("empty")}</p>
        ) : (
          withDisk.map((n) => {
            const total = n.totalBytes ?? 0;
            const free = n.availableBytes ?? 0;
            const used = Math.max(0, total - free);
            const usedPct = total > 0 ? (used / total) * 100 : 0;
            const freePct = Math.round(100 - usedPct);
            const tone =
              usedPct >= 90
                ? "bg-destructive"
                : usedPct >= 75
                  ? "bg-warning"
                  : "bg-success";
            return (
              <div key={n.slug} className="space-y-1">
                <div className="flex items-center justify-between gap-3 text-xs">
                  <span className="truncate font-medium text-foreground">{n.name}</span>
                  <span className="shrink-0 text-muted-foreground tabular-nums">
                    {formatBytes(free)} libres / {formatBytes(total)} ({freePct}% libre)
                  </span>
                </div>
                <div className="h-1.5 w-full overflow-hidden rounded-full bg-muted">
                  <div
                    className={cn("h-full rounded-full", tone)}
                    style={{ width: `${Math.max(2, Math.min(100, usedPct))}%` }}
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
