import { getTranslations } from "next-intl/server";
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

export async function CheckHistoryTable({ checks }: Props) {
  const t = await getTranslations("pages.monitors_detail.check_history");

  if (checks.length === 0) {
    return <EmptyState title={t("empty")} />;
  }
  const newestFirst = [...checks].reverse();
  return (
    <Card>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{t("col_when")}</TableHead>
            <TableHead>{t("col_status")}</TableHead>
            <TableHead>{t("col_http")}</TableHead>
            <TableHead>{t("col_latency")}</TableHead>
            <TableHead>{t("col_detail")}</TableHead>
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
                {c.httpStatusCode ?? "—"}
              </TableCell>
              <TableCell className="font-mono text-xs">
                {c.latencyMs === null ? "—" : `${c.latencyMs} ms`}
              </TableCell>
              <TableCell className="text-xs text-muted-foreground">
                {c.errorMessage ? (
                  <span className="text-destructive">{c.errorMessage}</span>
                ) : c.responseSnippet ? (
                  <span title={c.responseSnippet} className="line-clamp-1">
                    {c.responseSnippet}
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
