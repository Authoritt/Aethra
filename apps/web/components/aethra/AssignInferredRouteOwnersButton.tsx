"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2, Tags } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";
import type { PublicEndpointOwnerAssignmentResultDto } from "@/lib/types";

export function AssignInferredRouteOwnersButton({ count }: { count: number }) {
  const router = useRouter();
  const [busy, setBusy] = useState<"dry-run" | "apply" | null>(null);

  async function run(dryRun: boolean) {
    setBusy(dryRun ? "dry-run" : "apply");
    try {
      const result = await api<PublicEndpointOwnerAssignmentResultDto>(
        "/api/ops/public-endpoints/assign-inferred-owners",
        {
          method: "POST",
          body: JSON.stringify({ dryRun }),
        },
      );
      const failed = result.actions.filter((action) => action.status === "failed");
      if (failed.length > 0) {
        toast.error(failed.map((action) => action.errorMessage ?? action.message).join(" | "));
      } else if (dryRun) {
        toast.success(`${result.routeCount} route(s) listas para asignar owner`);
      } else {
        toast.success(`${result.routeCount} route(s) actualizadas`);
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
      setBusy(null);
    }
  }

  if (count <= 0) {
    return null;
  }

  return (
    <div className="flex flex-wrap gap-2">
      <Button
        type="button"
        size="sm"
        variant="outline"
        onClick={() => run(true)}
        disabled={busy !== null}
      >
        {busy === "dry-run" ? (
          <Loader2 className="mr-2 h-4 w-4 animate-spin" />
        ) : (
          <Tags className="mr-2 h-4 w-4" />
        )}
        Revisar owners ({count})
      </Button>
      <Button
        type="button"
        size="sm"
        onClick={() => run(false)}
        disabled={busy !== null}
      >
        {busy === "apply" ? (
          <Loader2 className="mr-2 h-4 w-4 animate-spin" />
        ) : (
          <Tags className="mr-2 h-4 w-4" />
        )}
        Asignar owners
      </Button>
    </div>
  );
}
