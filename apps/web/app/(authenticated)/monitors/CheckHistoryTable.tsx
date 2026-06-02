import { Card } from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { EmptyState } from "@/components/ui/empty-state";
import type { MonitorCheckDto } from "@/lib/types";
import { MonitorStatusPill } from "./MonitorStatusPill";

interface Props {
  checks: MonitorCheckDto[];
}

export function CheckHistoryTable({ checks }: Props) {
  if (checks.length === 0) {
    return <EmptyState title="Sin checks registrados" />;
  }
  const newestFirst = [...checks].reverse();
  return (
    <Card>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Cuando</TableHead>
            <TableHead>Estado</TableHead>
            <TableHead>HTTP</TableHead>
            <TableHead>Latencia</TableHead>
            <TableHead>Detalle</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {newestFirst.map((c) => (
            <TableRow key={c.id}>
              <TableCell className="whitespace-nowrap font-mono text-xs">
                {formatStamp(c.timestamp)}
              </TableCell>
              <TableCell>
                <MonitorStatusPill status={c.status} />
              </TableCell>
              <TableCell className="font-mono text-xs">
                {c.http_status_code ?? "—"}
              </TableCell>
              <TableCell className="font-mono text-xs">
                {c.latency_ms === null ? "—" : `${c.latency_ms} ms`}
              </TableCell>
              <TableCell className="text-xs text-muted-foreground">
                {c.error_message ? (
                  <span className="text-destructive">{c.error_message}</span>
                ) : c.response_snippet ? (
                  <span title={c.response_snippet} className="line-clamp-1">
                    {c.response_snippet}
                  </span>
                ) : (
                  "—"
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Card>
  );
}

function formatStamp(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}
