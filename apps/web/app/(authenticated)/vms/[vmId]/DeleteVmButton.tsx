"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useRouter } from "next/navigation";
import { Loader2, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";

/** Botón de borrado de VM (DELETE /api/vms/{id}?force=true). */
export function DeleteVmButton({ vmId, name }: { vmId: string; name: string }) {
  const c = useTranslations("common");
  const d = useTranslations("components.destructive");
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function del() {
    if (!confirm(d("confirm_vm", { name }))) {
      return;
    }
    setBusy(true);
    try {
      await api(`/api/vms/${encodeURIComponent(vmId)}?force=true`, { method: "DELETE" });
      toast.success("VM borrada");
      router.push("/vms");
      router.refresh();
    } catch (e) {
      toast.error(
        e instanceof ApiError
          ? ((e.body as { message?: string } | undefined)?.message ?? `Error ${e.status}`)
          : d("error_vm"),
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <Button type="button" variant="ghost" size="sm" onClick={del} disabled={busy}
      className="text-destructive hover:bg-destructive/10 hover:text-destructive">
      {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Trash2 className="mr-2 h-4 w-4" />} Borrar VM
    </Button>
  );
}
