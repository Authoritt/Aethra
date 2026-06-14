"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2, SlidersHorizontal } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { ApiError, api } from "@/lib/api";
import { NOTIFICATION_EVENT_TYPES } from "@/lib/types";

/**
 * Edita en sitio a qué eventos se suscribe un canal (PATCH eventFilters), sin tener que
 * borrar y recrear. El endpoint PATCH ya lo soportaba; la UI sólo exponía el toggle de activo.
 * Sin seleccionar ninguno = el canal recibe todos los eventos (semántica del dominio: filtros
 * vacíos = match all).
 */
export function EditEventsButton({
  id,
  name,
  eventFilters,
}: {
  id: string;
  name: string;
  eventFilters: string[];
}) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [selected, setSelected] = useState<string[]>(eventFilters);

  function toggle(ev: string) {
    setSelected((prev) =>
      prev.includes(ev) ? prev.filter((e) => e !== ev) : [...prev, ev],
    );
  }

  async function onSave() {
    setLoading(true);
    try {
      await api(`/api/notifications/channels/${encodeURIComponent(id)}`, {
        method: "PATCH",
        body: JSON.stringify({ eventFilters: selected }),
      });
      toast.success(`Eventos de "${name}" actualizados`);
      router.refresh();
      setOpen(false);
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  return (
    <>
      <Button
        variant="outline"
        size="sm"
        onClick={() => {
          setSelected(eventFilters);
          setOpen(true);
        }}
      >
        <SlidersHorizontal className="mr-2 h-4 w-4" />
        Eventos
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Eventos de &quot;{name}&quot;</DialogTitle>
            <DialogDescription>
              Elige a qué eventos se suscribe este canal. Sin seleccionar
              ninguno, recibe todos los eventos.
            </DialogDescription>
          </DialogHeader>
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            {NOTIFICATION_EVENT_TYPES.map((ev) => (
              <label
                key={ev}
                className="flex items-center gap-2 rounded-md border border-border bg-muted/20 p-2"
              >
                <Checkbox
                  checked={selected.includes(ev)}
                  onCheckedChange={() => toggle(ev)}
                />
                <span className="font-mono text-xs">{ev}</span>
              </label>
            ))}
          </div>
          <p className="text-xs text-muted-foreground">
            {selected.length === 0
              ? "Sin filtros: el canal recibirá todos los eventos."
              : `${selected.length} evento(s) seleccionado(s).`}
          </p>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setOpen(false)}>
              Cancelar
            </Button>
            <Button onClick={onSave} disabled={loading}>
              {loading ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
              Guardar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
