"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, Search } from "lucide-react";
import { toast } from "sonner";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError, api } from "@/lib/api";
import type {
  GlobalSearchResultDto,
  PublicAccessReconcileResultDto,
  PublicAccessVerificationResultDto,
} from "@/lib/types";

const MIN_QUERY_LENGTH = 2;

const TYPE_LABELS: Record<string, string> = {
  app: "App",
  app_environment: "App Environment",
  release: "Release",
  public_endpoint: "Public Endpoint",
  machine: "Machine",
  data_service: "Data Service",
  command: "Command",
};

const QUICK_COMMANDS: GlobalSearchResultDto[] = [
  {
    type: "command",
    title: "Broken public endpoints",
    subtitle: "Open Public Access filtered by broken health.",
    href: "/public-access?health=broken",
    status: "filter",
    badge: "Public Access",
    score: 100,
  },
  {
    type: "command",
    title: "Critical operational issues",
    subtitle: "Open the issues inbox filtered by critical severity.",
    href: "/operational-issues?severity=critical",
    status: "filter",
    badge: "Issues",
    score: 100,
  },
  {
    type: "command",
    title: "Offline machines",
    subtitle: "Open Machines filtered by offline readiness.",
    href: "/vms?readiness=offline",
    status: "filter",
    badge: "Machines",
    score: 100,
  },
  {
    type: "command",
    title: "Config drift",
    subtitle: "Open issues caused by effective config changes.",
    href: "/operational-issues?q=config.",
    status: "filter",
    badge: "Config",
    score: 100,
  },
  {
    type: "command",
    title: "Deploying releases",
    subtitle: "Open Releases filtered by active deployments.",
    href: "/releases?status=deploying",
    status: "filter",
    badge: "Releases",
    score: 100,
  },
  {
    type: "command",
    title: "Production app environments",
    subtitle: "Open App Environments filtered by production.",
    href: "/app-environments?environment=production",
    status: "filter",
    badge: "App Environments",
    score: 100,
  },
];

export function CommandPalette() {
  const router = useRouter();
  const inputRef = useRef<HTMLInputElement>(null);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<GlobalSearchResultDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const trimmedQuery = useMemo(() => query.trim(), [query]);
  const quickCommands = useMemo(
    () => filterQuickCommands(trimmedQuery),
    [trimmedQuery],
  );
  const visibleResults = useMemo(
    () => [...quickCommands, ...results].slice(0, 10),
    [quickCommands, results],
  );

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      const target = event.target as HTMLElement | null;
      const isTyping =
        target?.tagName === "INPUT" ||
        target?.tagName === "TEXTAREA" ||
        target?.isContentEditable;

      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setOpen((current) => !current);
        return;
      }

      if (!isTyping && event.key === "/") {
        event.preventDefault();
        setOpen(true);
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, []);

  useEffect(() => {
    if (!open) return;
    const frame = window.requestAnimationFrame(() => inputRef.current?.focus());
    return () => window.cancelAnimationFrame(frame);
  }, [open]);

  useEffect(() => {
    if (!open) return;

    if (trimmedQuery.length < MIN_QUERY_LENGTH) {
      return;
    }

    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      setLoading(true);
      setError(null);
      api<GlobalSearchResultDto[]>(
        `/api/ops/search?q=${encodeURIComponent(trimmedQuery)}&limit=8`,
        { signal: controller.signal },
      )
        .then((items) => setResults(items))
        .catch((err: unknown) => {
          if (controller.signal.aborted) return;
          setResults([]);
          setError(err instanceof Error ? err.message : "Search failed");
        })
        .finally(() => {
          if (!controller.signal.aborted) setLoading(false);
        });
    }, 200);

    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [open, trimmedQuery]);

  function navigateTo(result: GlobalSearchResultDto) {
    setOpen(false);
    setQuery("");
    setResults([]);
    router.push(result.href);
  }

  async function runPublicAccessAction(
    appEnvironmentId: string,
    action: "verify" | "dry-run" | "reconcile" | "deploy-native",
  ) {
    const key = `${action}:${appEnvironmentId}`;
    setBusyAction(key);
    try {
      if (action === "verify") {
        const result = await api<PublicAccessVerificationResultDto>(
          `/api/ops/public-access-states/${encodeURIComponent(appEnvironmentId)}/verify`,
          { method: "POST" },
        );
        const failed = result.checks.filter((check) => check.status === "failed");
        toast[failed.length > 0 ? "error" : "success"](
          failed.length > 0
            ? `${failed.length} check(s) fallaron`
            : `${result.checks.length} check(s) OK`,
        );
      } else if (action === "deploy-native") {
        const result = await api<{ healthy: boolean; services: string[] }>(
          `/api/instances/${encodeURIComponent(appEnvironmentId)}/deploy-native`,
          {
            method: "POST",
            body: JSON.stringify({}),
          },
        );
        toast.success(
          `Deploy nativo OK · ${result.services.length} servicio(s)${result.healthy ? " · healthy" : ""}`,
        );
      } else {
        const dryRun = action === "dry-run";
        const result = await api<PublicAccessReconcileResultDto>(
          `/api/ops/public-access-states/${encodeURIComponent(appEnvironmentId)}/reconcile`,
          {
            method: "POST",
            body: JSON.stringify({ dryRun }),
          },
        );
        const failed = result.actions.filter((item) => item.status === "failed" || item.status === "blocked");
        const changed = result.actions.filter((item) => item.status === "applied" || item.status === "planned");
        toast[failed.length > 0 ? "error" : "success"](
          failed.length > 0
            ? failed.map((item) => item.errorMessage ?? item.message).join(" | ")
            : `${changed.length} accion(es) ${dryRun ? "planeadas" : "reconciliadas"}`,
        );
      }
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string; Message?: string } | undefined)
              ?.message ??
            (e.body as { detail?: string } | undefined)?.detail ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setBusyAction(null);
    }
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        aria-label="Open command palette"
        onClick={() => setOpen(true)}
      >
        <Search className="size-4" />
      </Button>
      <DialogContent className="top-[15%] max-w-2xl translate-y-0 gap-3 p-0 sm:rounded-lg">
        <DialogHeader className="sr-only">
          <DialogTitle>Command Palette</DialogTitle>
          <DialogDescription>
            Search across apps, app environments, releases, endpoints, machines
            and services.
          </DialogDescription>
        </DialogHeader>
        <div className="flex items-center gap-2 border-b px-4 py-3">
          <Search className="size-4 text-muted-foreground" />
          <Input
            ref={inputRef}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Search apps, environments, endpoints..."
            className="h-9 border-0 px-0 shadow-none focus-visible:ring-0 focus-visible:ring-offset-0"
          />
          {loading && trimmedQuery.length >= MIN_QUERY_LENGTH ? (
            <Loader2 className="size-4 animate-spin text-muted-foreground" />
          ) : null}
        </div>

        <div className="max-h-[420px] overflow-y-auto px-2 pb-2">
          {trimmedQuery.length < MIN_QUERY_LENGTH ? (
            <CommandPaletteHint />
          ) : error ? (
            <p className="px-3 py-8 text-center text-sm text-destructive">
              {error}
            </p>
          ) : visibleResults.length === 0 && !loading ? (
            <p className="px-3 py-8 text-center text-sm text-muted-foreground">
              No results found.
            </p>
          ) : (
            <div className="py-2">
              {visibleResults.map((result) => {
                const appEnvironmentId = parseAppEnvironmentId(result.href);
                return (
                  <div
                    key={`${result.type}:${result.href}`}
                    className="rounded-md transition hover:bg-accent focus-within:bg-accent"
                  >
                    <button
                      type="button"
                      onClick={() => navigateTo(result)}
                      className="flex w-full items-center gap-3 px-3 py-2.5 text-left focus-visible:outline-none"
                    >
                      <div className="min-w-0 flex-1">
                        <div className="flex min-w-0 items-center gap-2">
                          <span className="truncate text-sm font-medium">
                            {result.title}
                          </span>
                          {result.status ? (
                            <Badge variant="outline" className="shrink-0 text-[10px]">
                              {result.status}
                            </Badge>
                          ) : null}
                        </div>
                        <p className="mt-1 truncate text-xs text-muted-foreground">
                          {result.subtitle}
                        </p>
                      </div>
                      <span className="shrink-0 text-xs text-muted-foreground">
                        {TYPE_LABELS[result.type] ?? result.type}
                      </span>
                    </button>
                    {appEnvironmentId ? (
                      <div className="flex flex-wrap gap-2 px-3 pb-2">
                        <PaletteActionButton
                          busy={busyAction === `verify:${appEnvironmentId}`}
                          disabled={busyAction !== null}
                          label="Verify"
                          onClick={() => runPublicAccessAction(appEnvironmentId, "verify")}
                        />
                        <PaletteActionButton
                          busy={busyAction === `dry-run:${appEnvironmentId}`}
                          disabled={busyAction !== null}
                          label="Dry run"
                          onClick={() => runPublicAccessAction(appEnvironmentId, "dry-run")}
                        />
                        <PaletteActionButton
                          busy={busyAction === `reconcile:${appEnvironmentId}`}
                          disabled={busyAction !== null}
                          label="Reconcile"
                          onClick={() => runPublicAccessAction(appEnvironmentId, "reconcile")}
                        />
                        <PaletteActionButton
                          busy={busyAction === `deploy-native:${appEnvironmentId}`}
                          disabled={busyAction !== null}
                          label="Deploy"
                          onClick={() => runPublicAccessAction(appEnvironmentId, "deploy-native")}
                        />
                      </div>
                    ) : null}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function CommandPaletteHint() {
  return (
    <div className="space-y-3 px-3 py-4 text-sm text-muted-foreground">
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="rounded-md border bg-muted/30 p-3">
          <p className="font-medium text-foreground">Jump to operations</p>
          <p className="mt-1 text-xs">
            Find an App Environment, release, machine, endpoint or data service.
          </p>
        </div>
        <div className="rounded-md border bg-muted/30 p-3">
          <p className="font-medium text-foreground">Shortcuts</p>
          <p className="mt-1 text-xs">Press Ctrl/Command+K or / from anywhere.</p>
        </div>
      </div>
      <div className="grid gap-2 sm:grid-cols-2">
        {QUICK_COMMANDS.slice(0, 4).map((command) => (
          <div key={command.href} className="rounded-md border px-3 py-2">
            <p className="truncate text-xs font-medium text-foreground">
              {command.title}
            </p>
            <p className="mt-1 truncate text-[11px]">{command.subtitle}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

function filterQuickCommands(query: string) {
  if (query.length < MIN_QUERY_LENGTH) return [];
  return QUICK_COMMANDS.filter((command) => {
    const haystack = [
      command.title,
      command.subtitle,
      command.href,
      command.status,
      command.badge,
    ]
      .filter(Boolean)
      .join(" ")
      .toLowerCase();
    return haystack.includes(query.toLowerCase());
  });
}

function parseAppEnvironmentId(href: string) {
  const match = /^\/instances\/([^/?#]+)/.exec(href);
  return match ? decodeURIComponent(match[1]) : null;
}

function PaletteActionButton({
  busy,
  disabled,
  label,
  onClick,
}: {
  busy: boolean;
  disabled: boolean;
  label: string;
  onClick: () => void;
}) {
  return (
    <Button
      type="button"
      size="sm"
      variant="outline"
      className="h-7 px-2 text-[11px]"
      onClick={onClick}
      disabled={disabled}
    >
      {busy ? <Loader2 className="mr-1.5 h-3 w-3 animate-spin" /> : null}
      {label}
    </Button>
  );
}
