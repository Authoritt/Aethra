"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useTranslations } from "next-intl";
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
  const t = useTranslations("pages.settings_users.actions");
  const router = useRouter();
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [resetOpen, setResetOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [newPassword, setNewPassword] = useState("");

  async function onDelete() {
    setLoading(true);
    try {
      await api(`/api/identity/users/${id}`, { method: "DELETE" });
      toast.success(t("deactivate_toast"));
      router.refresh();
    } catch (e) {
      toast.error(extractMsg(e, t("error_unknown")));
    } finally {
      setLoading(false);
      setConfirmDelete(false);
    }
  }

  async function onResetPassword() {
    if (newPassword.length < 8) {
      toast.error(t("reset_validation_short"));
      return;
    }
    setLoading(true);
    try {
      await api(`/api/identity/users/${id}/reset-password`, {
        method: "POST",
        body: JSON.stringify({ newPassword }),
      });
      toast.success(t("reset_toast"));
      setResetOpen(false);
      setNewPassword("");
      router.refresh();
    } catch (e) {
      toast.error(extractMsg(e, t("error_unknown")));
    } finally {
      setLoading(false);
    }
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button type="button" variant="ghost" size="icon" aria-label={t("menu_aria")}>
            <MoreHorizontal className="h-4 w-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem onSelect={() => setResetOpen(true)}>
            <Key className="size-4" />
            {t("menu_reset")}
          </DropdownMenuItem>
          {isActive && (
            <>
              <DropdownMenuSeparator />
              <DropdownMenuItem
                onSelect={() => setConfirmDelete(true)}
                className="text-destructive focus:bg-destructive/10 focus:text-destructive"
              >
                <Trash2 className="size-4" />
                {t("menu_deactivate")}
              </DropdownMenuItem>
            </>
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      <Dialog open={confirmDelete} onOpenChange={setConfirmDelete}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("deactivate_dialog_title", { email })}</DialogTitle>
            <DialogDescription>
              {t("deactivate_dialog_description")}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setConfirmDelete(false)}>
              {t("deactivate_cancel")}
            </Button>
            <Button
              variant="destructive"
              onClick={onDelete}
              disabled={loading}
            >
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              {t("deactivate_confirm")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={resetOpen} onOpenChange={setResetOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("reset_dialog_title", { email })}</DialogTitle>
            <DialogDescription>{t("reset_dialog_description")}</DialogDescription>
          </DialogHeader>
          <div className="flex flex-col gap-2 py-2">
            <Label htmlFor="new-pass">{t("reset_label")}</Label>
            <Input
              id="new-pass"
              type="password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              placeholder={t("reset_placeholder")}
              autoComplete="new-password"
            />
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setResetOpen(false)}>
              {t("reset_cancel")}
            </Button>
            <Button onClick={onResetPassword} disabled={loading}>
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              {t("reset_submit")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

function extractMsg(e: unknown, fallback: string): string {
  if (e instanceof ApiError) {
    const body = e.body as { message?: string; detail?: string } | undefined;
    return body?.message ?? body?.detail ?? `Error ${e.status}`;
  }
  if (e instanceof Error) return e.message;
  return fallback;
}
