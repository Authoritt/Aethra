"use client";

import { useEffect, useState } from "react";
import { Loader2, Play, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { ApiError, api } from "@/lib/api";
import type {
  ScheduledJobDto,
  ScheduledJobRunDto,
  ScheduledJobRunStatus,
} from "@/lib/types";

const STATUS_VARIANTS: Record<ScheduledJobRunStatus, "success" | "warning" | "outline"> = {
  Completed: "success",
  Failed: "warning",
  TimedOut: "warning",
  Cancelled: "outline",
  Running: "outline",
};

export function ScheduledJobsTab({ serviceId }: { serviceId: string }) {
  const [jobs, setJobs] = useState<ScheduledJobDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [showCreate, setShowCreate] = useState(false);
  const [busy, setBusy] = useState(false);
  const [openRunsFor, setOpenRunsFor] = useState<ScheduledJobDto | null>(null);

  async function load() {
    setLoading(true);
    try {
      const data = await api<ScheduledJobDto[]>(
        `/api/services/${encodeURIComponent(serviceId)}/scheduled-jobs`,
      );
      setJobs(data);
    } catch (e) {
      toast.error(formatError(e, "Failed to load scheduled jobs"));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [serviceId]);

  async function triggerNow(jobId: string) {
    setBusy(true);
    try {
      await api(`/api/scheduled-jobs/${encodeURIComponent(jobId)}/run-now`, {
        method: "POST",
      });
      toast.success("Run queued");
      await load();
    } catch (e) {
      toast.error(formatError(e, "Failed to trigger run"));
    } finally {
      setBusy(false);
    }
  }

  async function deleteJob(jobId: string) {
    if (!confirm("Delete this scheduled job? Existing runs history will be removed.")) {
      return;
    }
    setBusy(true);
    try {
      await api(`/api/scheduled-jobs/${encodeURIComponent(jobId)}`, {
        method: "DELETE",
      });
      toast.success("Job deleted");
      await load();
    } catch (e) {
      toast.error(formatError(e, "Failed to delete job"));
    } finally {
      setBusy(false);
    }
  }

  async function toggleEnabled(job: ScheduledJobDto) {
    setBusy(true);
    try {
      await api(`/api/scheduled-jobs/${encodeURIComponent(job.id)}`, {
        method: "PATCH",
        body: JSON.stringify({ enabled: !job.enabled }),
      });
      await load();
    } catch (e) {
      toast.error(formatError(e, "Failed to update job"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
          Scheduled jobs ({jobs.length})
        </h2>
        <Button size="sm" onClick={() => setShowCreate(true)}>
          <Plus className="mr-2 h-4 w-4" />
          New scheduled job
        </Button>
      </div>

      {loading ? (
        <div className="flex items-center gap-2 text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" /> Loading...
        </div>
      ) : jobs.length === 0 ? (
        <EmptyState
          title="No scheduled jobs"
          description="Create your first cron job. Examples: nightly pg_dump, periodic cleanup, healthcheck heartbeats."
        />
      ) : (
        <Card>
          <CardContent className="p-0">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Cron (TZ)</TableHead>
                  <TableHead>Command</TableHead>
                  <TableHead>Last / Next</TableHead>
                  <TableHead>Enabled</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {jobs.map((j) => (
                  <TableRow key={j.id}>
                    <TableCell>
                      <div className="font-medium">{j.name}</div>
                      {j.description ? (
                        <div className="text-xs text-muted-foreground">
                          {j.description}
                        </div>
                      ) : null}
                    </TableCell>
                    <TableCell>
                      <div className="font-mono text-xs">{j.cronExpression}</div>
                      <div className="text-[10px] uppercase text-muted-foreground">
                        {j.timeZone}
                      </div>
                    </TableCell>
                    <TableCell>
                      <code className="font-mono text-xs whitespace-pre-wrap break-all">
                        {j.command.slice(0, 80)}
                        {j.command.length > 80 ? "…" : ""}
                      </code>
                    </TableCell>
                    <TableCell>
                      <div className="text-xs">
                        last: {formatDateTime(j.lastRunAt)}
                      </div>
                      <div className="text-xs text-muted-foreground">
                        next: {formatDateTime(j.nextRunAt)}
                      </div>
                    </TableCell>
                    <TableCell>
                      <Badge variant={j.enabled ? "success" : "outline"}>
                        {j.enabled ? "enabled" : "disabled"}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex justify-end gap-1">
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={busy}
                          onClick={() => toggleEnabled(j)}
                        >
                          {j.enabled ? "Disable" : "Enable"}
                        </Button>
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={busy}
                          onClick={() => setOpenRunsFor(j)}
                        >
                          History
                        </Button>
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={busy}
                          onClick={() => triggerNow(j.id)}
                        >
                          <Play className="h-4 w-4" />
                        </Button>
                        <Button
                          size="sm"
                          variant="destructive"
                          disabled={busy}
                          onClick={() => deleteJob(j.id)}
                        >
                          <Trash2 className="h-4 w-4" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}

      <CreateJobDialog
        open={showCreate}
        serviceId={serviceId}
        onClose={() => setShowCreate(false)}
        onCreated={() => {
          setShowCreate(false);
          void load();
        }}
      />

      <JobRunsDialog
        job={openRunsFor}
        onClose={() => setOpenRunsFor(null)}
      />
    </section>
  );
}

function CreateJobDialog({
  open,
  serviceId,
  onClose,
  onCreated,
}: {
  open: boolean;
  serviceId: string;
  onClose: () => void;
  onCreated: () => void;
}) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [command, setCommand] = useState("");
  const [cron, setCron] = useState("0 2 * * *");
  const [timeZone, setTimeZone] = useState("UTC");
  const [timeoutSeconds, setTimeoutSeconds] = useState(300);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (open) {
      setName("");
      setDescription("");
      setCommand("");
      setCron("0 2 * * *");
      setTimeZone("UTC");
      setTimeoutSeconds(300);
    }
  }, [open]);

  async function submit() {
    setBusy(true);
    try {
      await api(
        `/api/services/${encodeURIComponent(serviceId)}/scheduled-jobs`,
        {
          method: "POST",
          body: JSON.stringify({
            name,
            description: description || null,
            command,
            cronExpression: cron,
            timeZone,
            timeoutSeconds,
          }),
        },
      );
      toast.success("Scheduled job created");
      onCreated();
    } catch (e) {
      toast.error(formatError(e, "Failed to create job"));
    } finally {
      setBusy(false);
    }
  }

  const human = describeCron(cron);

  return (
    <Dialog open={open} onOpenChange={(v) => (v ? null : onClose())}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>New scheduled job</DialogTitle>
          <DialogDescription>
            Runs inside the service container via <code>docker exec</code>.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label htmlFor="job-name">Name</Label>
            <Input
              id="job-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="nightly-pg-dump"
            />
          </div>
          <div>
            <Label htmlFor="job-desc">Description (optional)</Label>
            <Input
              id="job-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
            />
          </div>
          <div>
            <Label htmlFor="job-cmd">Command</Label>
            <Textarea
              id="job-cmd"
              value={command}
              onChange={(e) => setCommand(e.target.value)}
              placeholder="pg_dump -U postgres myapp > /backup/$(date +%Y%m%d).sql"
              rows={3}
              className="font-mono text-sm"
            />
          </div>
          <div className="grid grid-cols-3 gap-3">
            <div className="col-span-2">
              <Label htmlFor="job-cron">Cron</Label>
              <Input
                id="job-cron"
                value={cron}
                onChange={(e) => setCron(e.target.value)}
                placeholder="0 2 * * *"
                className="font-mono"
              />
              <div className="mt-1 text-xs text-muted-foreground">{human}</div>
            </div>
            <div>
              <Label htmlFor="job-tz">TZ</Label>
              <Input
                id="job-tz"
                value={timeZone}
                onChange={(e) => setTimeZone(e.target.value)}
                placeholder="UTC"
              />
            </div>
          </div>
          <div>
            <Label htmlFor="job-timeout">Timeout (s)</Label>
            <Input
              id="job-timeout"
              type="number"
              min={1}
              max={86400}
              value={timeoutSeconds}
              onChange={(e) => setTimeoutSeconds(Number(e.target.value) || 300)}
              className="w-32"
            />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button onClick={submit} disabled={busy || !name || !command}>
            {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            Create job
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function JobRunsDialog({
  job,
  onClose,
}: {
  job: ScheduledJobDto | null;
  onClose: () => void;
}) {
  const [runs, setRuns] = useState<ScheduledJobRunDto[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!job) {
      setRuns([]);
      return;
    }
    setLoading(true);
    api<ScheduledJobRunDto[]>(
      `/api/scheduled-jobs/${encodeURIComponent(job.id)}/runs?limit=50`,
    )
      .then(setRuns)
      .catch((e) => toast.error(formatError(e, "Failed to load runs")))
      .finally(() => setLoading(false));
  }, [job]);

  return (
    <Dialog open={Boolean(job)} onOpenChange={(v) => (v ? null : onClose())}>
      <DialogContent className="sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>Run history — {job?.name}</DialogTitle>
          <DialogDescription>Most recent 50 runs.</DialogDescription>
        </DialogHeader>
        {loading ? (
          <div className="flex items-center gap-2 text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" /> Loading...
          </div>
        ) : runs.length === 0 ? (
          <EmptyState title="No runs yet" description="Trigger one manually with the play button." />
        ) : (
          <div className="max-h-[60vh] overflow-y-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Started</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Exit</TableHead>
                  <TableHead>Duration</TableHead>
                  <TableHead>Output</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {runs.map((r) => (
                  <TableRow key={r.id}>
                    <TableCell className="font-mono text-xs">
                      {formatDateTime(r.startedAt)}
                    </TableCell>
                    <TableCell>
                      <Badge variant={STATUS_VARIANTS[r.status] ?? "outline"}>
                        {r.status}
                      </Badge>
                    </TableCell>
                    <TableCell className="font-mono text-xs">
                      {r.exitCode ?? "—"}
                    </TableCell>
                    <TableCell className="font-mono text-xs">
                      {r.durationMs != null ? `${r.durationMs}ms` : "—"}
                    </TableCell>
                    <TableCell>
                      {r.stdout || r.stderr ? (
                        <details>
                          <summary className="cursor-pointer text-xs text-primary">
                            view
                          </summary>
                          {r.stdout ? (
                            <pre className="mt-2 max-h-48 overflow-auto rounded bg-muted p-2 text-[10px]">
                              {r.stdout}
                            </pre>
                          ) : null}
                          {r.stderr ? (
                            <pre className="mt-2 max-h-48 overflow-auto rounded bg-destructive/10 p-2 text-[10px] text-destructive">
                              {r.stderr}
                            </pre>
                          ) : null}
                        </details>
                      ) : (
                        <span className="text-xs text-muted-foreground">—</span>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
        <DialogFooter>
          <Button onClick={onClose}>Close</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function formatError(e: unknown, fallback: string): string {
  if (e instanceof ApiError) {
    return (
      (e.body as { message?: string } | undefined)?.message ?? `Error ${e.status}`
    );
  }
  if (e instanceof Error) return e.message;
  return fallback;
}

function formatDateTime(iso: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  return d.toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/**
 * Cron parser ligero para mostrar una descripcion human-readable a partir del input.
 * Solo cubre los patrones comunes ('* * * * *', '0 H * * *', '0 0 * * D', '* / N').
 * Suficiente como hint visual; el backend valida estrictamente al crear.
 */
function describeCron(expr: string): string {
  const parts = expr.trim().split(/\s+/);
  if (parts.length !== 5) {
    return "Invalid cron (need 5 fields)";
  }
  const [min, hour, day, month, dow] = parts;
  if (min.startsWith("*/")) {
    return `Every ${min.slice(2)} minute(s)`;
  }
  if (hour === "*" && day === "*" && month === "*" && dow === "*" && /^\d+$/.test(min)) {
    return `Every hour at minute ${min}`;
  }
  if (day === "*" && month === "*" && dow === "*" && /^\d+$/.test(hour) && /^\d+$/.test(min)) {
    return `Every day at ${hour.padStart(2, "0")}:${min.padStart(2, "0")}`;
  }
  if (day === "*" && month === "*" && dow !== "*" && /^\d+$/.test(hour) && /^\d+$/.test(min)) {
    return `Weekly (DOW=${dow}) at ${hour.padStart(2, "0")}:${min.padStart(2, "0")}`;
  }
  return `Custom: ${expr}`;
}
