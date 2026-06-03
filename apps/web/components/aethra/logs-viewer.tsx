"use client";

import * as React from "react";
import { AlertTriangle, AlertCircle, Info } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

export interface LogEntry {
  sequence: number;
  timestamp: string;
  level?: "info" | "warn" | "error" | "debug" | string;
  stage?: string;
  text: string;
}

export interface LogsViewerProps {
  entries: LogEntry[];
  /** Indica si todavía hay tail en curso — muestra "live" indicator. */
  isLive?: boolean;
  className?: string;
  /** Altura mínima del viewer. Default `min-h-[400px] max-h-[640px]`. */
  heightClassName?: string;
}

type FilterLevel = "all" | "info" | "warn" | "error";

/**
 * Visor de logs en vivo (build/deploy). Auto-scroll al fondo cuando llegan
 * entries nuevas — el usuario puede pausarlo desplazándose hacia arriba.
 */
export function LogsViewer({
  entries,
  isLive = false,
  className,
  heightClassName = "min-h-[400px] max-h-[640px]",
}: LogsViewerProps) {
  const t = useTranslations("components.logs_viewer");
  const containerRef = React.useRef<HTMLDivElement>(null);
  const [autoScroll, setAutoScroll] = React.useState(true);
  const [filter, setFilter] = React.useState<FilterLevel>("all");

  const filtered = React.useMemo(() => {
    if (filter === "all") return entries;
    return entries.filter((e) => normalizeLevel(e.level) === filter);
  }, [entries, filter]);

  // Auto-scroll al fondo cuando llegan entries y autoScroll está activo.
  React.useEffect(() => {
    if (!autoScroll || !containerRef.current) return;
    containerRef.current.scrollTop = containerRef.current.scrollHeight;
  }, [filtered, autoScroll]);

  function onScroll(e: React.UIEvent<HTMLDivElement>) {
    const el = e.currentTarget;
    const atBottom =
      Math.abs(el.scrollHeight - el.scrollTop - el.clientHeight) < 24;
    setAutoScroll(atBottom);
  }

  return (
    <div
      className={cn(
        "rounded-md border border-border bg-card text-card-foreground shadow-sm overflow-hidden flex flex-col",
        className,
      )}
    >
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border bg-muted/40 px-3 py-2">
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          {isLive ? (
            <span className="inline-flex items-center gap-1.5">
              <span className="relative inline-flex h-2 w-2">
                <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-info opacity-60" />
                <span className="relative inline-flex h-2 w-2 rounded-full bg-info" />
              </span>
              <span className="font-medium text-foreground">{t("live")}</span>
            </span>
          ) : (
            <span className="font-medium text-foreground">{t("logs_header")}</span>
          )}
          <span aria-hidden>·</span>
          <span>{t("lines_count", { count: filtered.length })}</span>
          {!autoScroll && isLive ? (
            <span className="ml-2 rounded bg-warning/15 px-1.5 py-0.5 text-[10px] font-medium text-warning-foreground">
              {t("autoscroll_paused")}
            </span>
          ) : null}
        </div>
        <div className="flex items-center gap-1">
          {(["all", "info", "warn", "error"] as const).map((lvl) => (
            <Button
              key={lvl}
              variant={filter === lvl ? "secondary" : "ghost"}
              size="sm"
              className="h-7 px-2 text-xs"
              onClick={() => setFilter(lvl)}
            >
              {lvl === "all" ? t("filter_all") : lvl}
            </Button>
          ))}
        </div>
      </div>

      <div
        ref={containerRef}
        onScroll={onScroll}
        className={cn(
          "overflow-y-auto bg-background/60 px-3 py-2 font-mono text-xs leading-relaxed",
          heightClassName,
        )}
      >
        {filtered.length === 0 ? (
          <div className="flex h-full items-center justify-center py-12 text-muted-foreground">
            <span>{isLive ? t("waiting_logs") : t("no_logs")}</span>
          </div>
        ) : (
          <ol className="space-y-0.5">
            {filtered.map((e) => (
              <LogRow key={e.sequence} entry={e} />
            ))}
          </ol>
        )}
      </div>
    </div>
  );
}

function LogRow({ entry }: { entry: LogEntry }) {
  const level = normalizeLevel(entry.level);
  return (
    <li
      className={cn(
        "grid grid-cols-[auto_auto_auto_1fr] items-baseline gap-2 rounded px-1 py-px",
        level === "error" && "bg-destructive/5 text-destructive-foreground",
        level === "warn" && "bg-warning/5",
      )}
    >
      <span className="text-muted-foreground/70 tabular-nums">
        {formatTs(entry.timestamp)}
      </span>
      <LevelIcon level={level} />
      {entry.stage ? (
        <span className="rounded bg-muted px-1 py-px text-[10px] font-medium uppercase tracking-wide text-muted-foreground">
          {entry.stage}
        </span>
      ) : (
        <span />
      )}
      <span className="whitespace-pre-wrap break-words text-foreground">
        {entry.text}
      </span>
    </li>
  );
}

function LevelIcon({ level }: { level: FilterLevel }) {
  if (level === "error")
    return <AlertCircle className="h-3 w-3 shrink-0 text-destructive" />;
  if (level === "warn")
    return <AlertTriangle className="h-3 w-3 shrink-0 text-warning" />;
  if (level === "info")
    return <Info className="h-3 w-3 shrink-0 text-info" />;
  return <span className="h-3 w-3" aria-hidden />;
}

function normalizeLevel(level: string | undefined): FilterLevel {
  if (!level) return "info";
  const l = level.toLowerCase();
  if (l === "error" || l === "err" || l === "fatal") return "error";
  if (l === "warn" || l === "warning") return "warn";
  if (l === "info" || l === "log") return "info";
  return "info";
}

function formatTs(ts: string): string {
  try {
    const d = new Date(ts);
    return d.toLocaleTimeString("es-ES", { hour12: false });
  } catch {
    return ts;
  }
}
