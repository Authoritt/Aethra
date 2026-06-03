"use client";

import * as React from "react";
import { Eye, EyeOff, Plus, Trash2, Upload } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { cn } from "@/lib/utils";

export interface EnvVar {
  key: string;
  value: string;
  isSecret: boolean;
}

export interface EnvVarsEditorProps {
  value: EnvVar[];
  onChange: (next: EnvVar[]) => void;
  className?: string;
  /** Permite duplicar keys (override) — por defecto las únicas válidas son keys distintas. */
  allowDuplicates?: boolean;
}

/**
 * Controlled editor de variables de entorno. No persiste — propaga via `onChange`.
 * Soporta import desde formato .env (KEY=value, comentarios, vacíos).
 */
export function EnvVarsEditor({
  value,
  onChange,
  className,
  allowDuplicates = false,
}: EnvVarsEditorProps) {
  const t = useTranslations("components.env_vars_editor");
  const [revealedKeys, setRevealedKeys] = React.useState<Record<string, boolean>>(
    {},
  );
  const [importOpen, setImportOpen] = React.useState(false);
  const [importText, setImportText] = React.useState("");
  const [importError, setImportError] = React.useState<string | null>(null);

  function update(idx: number, patch: Partial<EnvVar>) {
    const next = value.map((v, i) => (i === idx ? { ...v, ...patch } : v));
    onChange(next);
  }
  function remove(idx: number) {
    onChange(value.filter((_, i) => i !== idx));
  }
  function add() {
    onChange([...value, { key: "", value: "", isSecret: false }]);
  }
  function toggleReveal(idx: number) {
    setRevealedKeys((prev) => ({ ...prev, [String(idx)]: !prev[String(idx)] }));
  }

  function commitImport() {
    setImportError(null);
    try {
      const parsed = parseDotenv(importText);
      const next = [...value];
      for (const item of parsed) {
        const existing = next.findIndex((v) => v.key === item.key);
        if (existing >= 0 && !allowDuplicates) {
          next[existing] = {
            ...next[existing],
            value: item.value,
          };
        } else {
          next.push(item);
        }
      }
      onChange(next);
      setImportText("");
      setImportOpen(false);
    } catch (e) {
      setImportError(e instanceof Error ? e.message : t("parse_error"));
    }
  }

  return (
    <div className={cn("rounded-md border border-border bg-card", className)}>
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border px-3 py-2">
        <div className="text-sm">
          <span className="font-medium text-foreground">{t("title")}</span>
          <span className="ml-2 text-muted-foreground">{value.length}</span>
        </div>
        <div className="flex items-center gap-2">
          <Dialog open={importOpen} onOpenChange={setImportOpen}>
            <DialogTrigger asChild>
              <Button variant="outline" size="sm">
                <Upload className="mr-2 h-4 w-4" />
                {t("import_env")}
              </Button>
            </DialogTrigger>
            <DialogContent className="max-w-2xl">
              <DialogHeader>
                <DialogTitle>{t("import_dialog_title")}</DialogTitle>
                <DialogDescription>
                  {t("import_dialog_description")}
                </DialogDescription>
              </DialogHeader>
              <Textarea
                value={importText}
                onChange={(e) => setImportText(e.target.value)}
                placeholder={t("import_placeholder")}
                className="min-h-[200px] font-mono text-xs"
              />
              {importError ? (
                <p className="text-sm text-destructive">{importError}</p>
              ) : null}
              <DialogFooter>
                <Button variant="ghost" onClick={() => setImportOpen(false)}>
                  {t("cancel")}
                </Button>
                <Button onClick={commitImport}>{t("import")}</Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
          <Button size="sm" onClick={add}>
            <Plus className="mr-2 h-4 w-4" />
            {t("add")}
          </Button>
        </div>
      </div>

      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="w-[35%]">{t("col_key")}</TableHead>
            <TableHead>{t("col_value")}</TableHead>
            <TableHead className="w-[120px]">{t("col_secret")}</TableHead>
            <TableHead className="w-[80px]"></TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {value.length === 0 ? (
            <TableRow>
              <TableCell colSpan={4} className="h-24 text-center text-sm text-muted-foreground">
                {t("empty")}
              </TableCell>
            </TableRow>
          ) : (
            value.map((envVar, idx) => {
              const revealed = !!revealedKeys[String(idx)];
              const masked = envVar.isSecret && !revealed;
              return (
                <TableRow key={idx}>
                  <TableCell>
                    <Input
                      value={envVar.key}
                      onChange={(e) => update(idx, { key: e.target.value })}
                      placeholder="DATABASE_URL"
                      className="font-mono text-xs"
                    />
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-1">
                      <Input
                        type={masked ? "password" : "text"}
                        value={envVar.value}
                        onChange={(e) => update(idx, { value: e.target.value })}
                        placeholder={envVar.isSecret ? "••••••••" : t("col_value").toLowerCase()}
                        className="font-mono text-xs"
                      />
                      {envVar.isSecret ? (
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          className="h-8 w-8 shrink-0"
                          onClick={() => toggleReveal(idx)}
                          aria-label={revealed ? t("hide") : t("show")}
                        >
                          {revealed ? (
                            <EyeOff className="h-4 w-4" />
                          ) : (
                            <Eye className="h-4 w-4" />
                          )}
                        </Button>
                      ) : null}
                    </div>
                  </TableCell>
                  <TableCell>
                    <Switch
                      checked={envVar.isSecret}
                      onCheckedChange={(checked) =>
                        update(idx, { isSecret: checked })
                      }
                    />
                  </TableCell>
                  <TableCell>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      onClick={() => remove(idx)}
                      aria-label={t("delete")}
                    >
                      <Trash2 className="h-4 w-4 text-muted-foreground" />
                    </Button>
                  </TableCell>
                </TableRow>
              );
            })
          )}
        </TableBody>
      </Table>
    </div>
  );
}

function parseDotenv(text: string): EnvVar[] {
  const out: EnvVar[] = [];
  const lines = text.split(/\r?\n/);
  let i = 0;
  for (const raw of lines) {
    i++;
    const line = raw.trim();
    if (!line || line.startsWith("#")) continue;
    const eq = line.indexOf("=");
    if (eq < 0) {
      throw new Error(`Línea ${i}: falta '='`);
    }
    const key = line.slice(0, eq).trim();
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(key)) {
      throw new Error(`Línea ${i}: key inválida "${key}"`);
    }
    let value = line.slice(eq + 1).trim();
    // Quitar quotes
    if (
      (value.startsWith('"') && value.endsWith('"')) ||
      (value.startsWith("'") && value.endsWith("'"))
    ) {
      value = value.slice(1, -1);
    }
    const isSecret = /SECRET|PASSWORD|TOKEN|KEY|CREDENTIAL/i.test(key);
    out.push({ key, value, isSecret });
  }
  return out;
}
