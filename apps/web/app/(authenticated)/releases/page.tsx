import Link from "next/link";
import { redirect } from "next/navigation";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { ReleaseOverviewDto } from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function ReleasesPage() {
  const data = await serverFetch<ReleaseOverviewDto[]>("/api/ops/releases");
  if (data === "unauthorized") redirect("/login");
  const releases = Array.isArray(data) ? data : [];

  return (
    <div className="space-y-6 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Releases"
        description="Cada push o trigger manual como build + deploy fan-out + resultado."
      />

      {data === "error" ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar releases.
          </CardContent>
        </Card>
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Status</TableHead>
                <TableHead>App</TableHead>
                <TableHead>Ref</TableHead>
                <TableHead>SHA</TableHead>
                <TableHead>Fan-out</TableHead>
                <TableHead>Trigger</TableHead>
                <TableHead>Created</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {releases.map((release) => (
                <TableRow key={release.id}>
                  <TableCell><StatusBadge status={release.status} /></TableCell>
                  <TableCell>
                    {release.appId ? (
                      <Link href={`/apps/${release.appId}`} className="font-medium hover:text-primary">
                        {release.appName}
                      </Link>
                    ) : (
                      <span>{release.appName}</span>
                    )}
                  </TableCell>
                  <TableCell className="font-mono text-xs">{release.gitRef}</TableCell>
                  <TableCell>
                    <Link href={`/builds/${release.buildId}`} className="font-mono text-xs text-primary">
                      {release.shortSha}
                    </Link>
                  </TableCell>
                  <TableCell className="text-xs">
                    {release.completedCount} ok / {release.failedCount} failed / {release.activeCount} active
                  </TableCell>
                  <TableCell className="text-xs">{release.trigger}</TableCell>
                  <TableCell className="text-xs text-muted-foreground">{formatDate(release.createdAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}
    </div>
  );
}

function StatusBadge({ status }: { status: string }) {
  const variant = status === "healthy" ? "success" : status === "failed" ? "destructive" : status === "active" ? "warning" : "outline";
  return <Badge variant={variant} className="font-mono text-[10px] uppercase">{status}</Badge>;
}

function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });
}
