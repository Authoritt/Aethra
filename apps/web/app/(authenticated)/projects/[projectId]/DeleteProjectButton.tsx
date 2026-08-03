"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useRouter } from "next/navigation";
import { Loader2, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";

/**
 * Borrado de proyecto en cascada (templates, clients, instancias). Confirmación inline en dos
 * pasos para evitar borrados accidentales; no detiene contenedores ni rutas del proxy.
 */
export function DeleteProjectButton({
  projectId,
  projectName,
}: {
  projectId: string;
  projectName: string;
}) {
  const c = useTranslations("common");
  const d = useTranslations("components.destructive");
  const router = useRouter();
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);

  async function onDelete() {
    setBusy(true);
    try {
      await api(
        `/api/projects/${encodeURIComponent(projectId)}?force=true`,
        { method: "DELETE" },
      );
      toast.success(`Proyecto "${projectName}" eliminado`);
      router.push("/projects");
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? ((e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`)
          : e instanceof Error
            ? e.message
            : d("error_project");
      toast.error(msg);
      setBusy(false);
      setConfirming(false);
    }
  }

  if (!confirming) {
    return (
      <Button
        variant="outline"
        className="border-destructive/40 text-destructive hover:bg-destructive/10 hover:text-destructive"
        onClick={() => setConfirming(true)}
      >
        <Trash2 className="mr-2 h-4 w-4" />
        {c("delete")}
      </Button>
    );
  }

  return (
    <div className="flex items-center gap-2">
      <Button variant="ghost" onClick={() => setConfirming(false)} disabled={busy}>
        {c("cancel")}
      </Button>
      <Button variant="destructive" onClick={onDelete} disabled={busy}>
        {busy ? (
          <Loader2 className="mr-2 h-4 w-4 animate-spin" />
        ) : (
          <Trash2 className="mr-2 h-4 w-4" />
        )}
        Confirmar borrado
      </Button>
    </div>
  );
}
