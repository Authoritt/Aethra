"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { ApiError, api } from "@/lib/api";

export function AutoDeployToggle({
  instanceId,
  initial,
}: {
  instanceId: string;
  initial: boolean;
}) {
  const router = useRouter();
  const [enabled, setEnabled] = useState(initial);
  const [busy, setBusy] = useState(false);

  async function onChange(target: boolean) {
    setBusy(true);
    const path = target
      ? `/api/instances/${encodeURIComponent(instanceId)}/auto-deploy/enable`
      : `/api/instances/${encodeURIComponent(instanceId)}/auto-deploy/disable`;
    setEnabled(target);
    try {
      await api(path, { method: "POST" });
      toast.success(target ? "Auto-deploy activado" : "Auto-deploy desactivado");
      router.refresh();
    } catch (e) {
      setEnabled(!target);
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ?? `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex items-center gap-3">
      <Switch
        id="autodeploy"
        checked={enabled}
        onCheckedChange={onChange}
        disabled={busy}
      />
      <Label htmlFor="autodeploy" className="cursor-pointer">
        {enabled ? "Activado" : "Desactivado"}
      </Label>
    </div>
  );
}
