"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2, RefreshCw, SearchCheck } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";
import type { PublicAccessReconcileResultDto } from "@/lib/types";

export function PublicAccessReconcileActions({
  appEnvironmentId,
  disabled,
}: {
  appEnvironmentId: string;
  disabled?: boolean;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState<"dry-run" | "apply" | null>(null);

  async function run(dryRun: boolean) {
    setBusy(dryRun ? "dry-run" : "apply");
    try {
      const result = await api<PublicAccessReconcileResultDto>(
        `/api/ops/public-access-states/${encodeURIComponent(appEnvironmentId)}/reconcile`,
        {
          method: "POST",
          body: JSON.stringify({ dryRun }),
        },
      );

      const failed = result.actions.filter((action) => action.status === "failed");
      const blocked = result.actions.filter((action) => action.status === "blocked");
      const changed = result.actions.filter((action) =>
        action.status === "applied" || action.status === "planned",
      );

      if (failed.length > 0 || blocked.length > 0) {
        toast.error(
          [...failed, ...blocked]
            .map((action) => action.errorMessage ?? action.message)
            .join(" | "),
        );
      } else if (dryRun) {
        toast.success(`${changed.length} accion(es) planeadas`);
      } else {
        toast.success(`${changed.length} accion(es) reconciliadas`);
      }

      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string; Message?: string } | undefined)
              ?.message ?? (e.body as { Message?: string } | undefined)?.Message ?? `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="flex flex-wrap gap-2">
      <Button
        type="button"
        size="sm"
        variant="outline"
        onClick={() => run(true)}
        disabled={disabled || busy !== null}
      >
        {busy === "dry-run" ? (
          <Loader2 className="mr-2 h-4 w-4 animate-spin" />
        ) : (
          <SearchCheck className="mr-2 h-4 w-4" />
        )}
        Dry run
      </Button>
      <Button
        type="button"
        size="sm"
        onClick={() => run(false)}
        disabled={disabled || busy !== null}
      >
        {busy === "apply" ? (
          <Loader2 className="mr-2 h-4 w-4 animate-spin" />
        ) : (
          <RefreshCw className="mr-2 h-4 w-4" />
        )}
        Reconcile
      </Button>
    </div>
  );
}
