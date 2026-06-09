"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2, Radar } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";
import type { PublicAccessVerificationResultDto } from "@/lib/types";

export function PublicAccessVerifyButton({
  appEnvironmentId,
  disabled,
}: {
  appEnvironmentId: string;
  disabled?: boolean;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function verify() {
    setBusy(true);
    try {
      const result = await api<PublicAccessVerificationResultDto>(
        `/api/ops/public-access-states/${encodeURIComponent(appEnvironmentId)}/verify`,
        { method: "POST" },
      );
      const failed = result.checks.filter((check) => check.status === "failed");
      const blocked = result.checks.filter((check) => check.status === "blocked");
      if (failed.length > 0) {
        toast.error(
          failed
            .map((check) => {
              const reason = check.errorMessage ?? check.httpStatusCode ?? "failed";
              return `${check.label}${check.target ? ` (${check.target})` : ""}: ${reason}`;
            })
            .join(" | "),
        );
      } else if (blocked.length > 0) {
        toast.warning(
          blocked
            .map((check) => `${check.label}: ${check.errorMessage ?? "blocked"}`)
            .join(" | "),
        );
      } else {
        toast.success("Public Access verificado");
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
      setBusy(false);
    }
  }

  return (
    <Button
      type="button"
      size="sm"
      variant="outline"
      onClick={verify}
      disabled={disabled || busy}
    >
      {busy ? (
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
      ) : (
        <Radar className="mr-2 h-4 w-4" />
      )}
      Verify
    </Button>
  );
}
