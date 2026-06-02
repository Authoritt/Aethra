"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";
import { Switch } from "@/components/ui/switch";
import { ApiError, api } from "@/lib/api";

export function ToggleActiveSwitch({
  id,
  isActive,
}: {
  id: string;
  isActive: boolean;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [active, setActive] = useState(isActive);

  async function onChange(next: boolean) {
    setBusy(true);
    setActive(next);
    try {
      await api(`/api/notifications/channels/${encodeURIComponent(id)}`, {
        method: "PATCH",
        body: JSON.stringify({ isActive: next }),
      });
      router.refresh();
    } catch (e) {
      setActive(!next);
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Switch
      checked={active}
      onCheckedChange={onChange}
      disabled={busy}
      aria-label="Activo"
    />
  );
}
