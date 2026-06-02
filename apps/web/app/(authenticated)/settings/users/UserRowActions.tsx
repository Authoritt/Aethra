"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Key, Loader2, MoreHorizontal, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError, api } from "@/lib/api";

export function UserRowActions({
  id,
  email,
  isActive,
}: {
  id: string;
  email: string;
  isActive: boolean;
}) {
  const router = useRouter();
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [resetOpen, setResetOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [newPassword, setNewPassword] = useState("");

  async function onDelete() {
    setLoading(true);
    try {
      await api(`/api/identity/users/${id}`, { method: "DELETE" });
      toast.success("Usuario desactivado");
      router.refresh();
    } catch (e) {
      toast.error(extractMsg(e));
    } finally {
      setLoading(false);
      setConfirmDelete(false);
    }
  }

  async function onResetPassword() {
    if (newPassword.length < 8) {
      toast.error("La contraseña debe tener al menos 8 caracteres.");
      return;
    }
    setLoading(true);
    try {
      await api(`/api/identity/users/${id}/reset-password`, {
        method: "POST",
        body: JSON.stringify({ newPassword }),
      });
      toast.success("Contraseña restablecida. Compartila por canal seguro.");
      setResetOpen(false);
      setNewPassword("");
      router.refresh();
    } catch (e) {
      toast.error(extractMsg(e));
    } finally {
      setLoading(false);
    }
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button type="button" variant="ghost" size="icon">
            <MoreHorizontal className="h-4 w-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem onSelect={() => setResetOpen(true)}>
            <Key className="size-4" />
            Reset password
          </DropdownMenuItem>
          {isActive && (
            <>
              <DropdownMenuSeparator />
              <DropdownMenuItem
                onSelect={() => setConfirmDelete(true)}
                className="text-destructive focus:bg-destructive/10 focus:text-destructive"
              >
                <Trash2 className="size-4" />
                Desactivar
              </DropdownMenuItem>
            </>
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      <Dialog open={confirmDelete} onOpenChange={setConfirmDelete}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Desactivar usuario {email}</DialogTitle>
            <DialogDescription>
              El usuario no podrá iniciar sesión hasta que sea reactivado. Las
              referencias históricas (notas, deployments) se preservan.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setConfirmDelete(false)}>
              Cancelar
            </Button>
            <Button
              variant="destructive"
              onClick={onDelete}
              disabled={loading}
            >
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              Desactivar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={resetOpen} onOpenChange={setResetOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Reset contraseña — {email}</DialogTitle>
            <DialogDescription>
              Definí una nueva contraseña. Comunicala al usuario por un canal
              seguro — Aethra todavía no envía emails automáticos.
            </DialogDescription>
          </DialogHeader>
          <div className="flex flex-col gap-2 py-2">
            <Label htmlFor="new-pass">Nueva contraseña</Label>
            <Input
              id="new-pass"
              type="password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              placeholder="mínimo 8 caracteres"
              autoComplete="new-password"
            />
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setResetOpen(false)}>
              Cancelar
            </Button>
            <Button onClick={onResetPassword} disabled={loading}>
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              Resetear
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

function extractMsg(e: unknown): string {
  if (e instanceof ApiError) {
    const body = e.body as { message?: string; detail?: string } | undefined;
    return body?.message ?? body?.detail ?? `Error ${e.status}`;
  }
  if (e instanceof Error) return e.message;
  return "Error desconocido";
}
