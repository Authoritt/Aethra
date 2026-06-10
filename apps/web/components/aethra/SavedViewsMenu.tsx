"use client";

import { useEffect, useMemo, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Bookmark, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

interface SavedView {
  name: string;
  href: string;
}

export function SavedViewsMenu({ storageKey }: { storageKey: string }) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const [views, setViews] = useState<SavedView[]>([]);

  const currentHref = useMemo(() => {
    const query = searchParams.toString();
    return query ? `${pathname}?${query}` : pathname;
  }, [pathname, searchParams]);

  useEffect(() => {
    const frame = window.requestAnimationFrame(() => {
      setViews(readViews(storageKey));
    });
    return () => window.cancelAnimationFrame(frame);
  }, [storageKey]);

  function saveCurrentView() {
    const fallbackName = searchParams.toString() || "Default view";
    const name = window.prompt("Nombre de la vista", fallbackName);
    if (!name?.trim()) return;

    const next = [
      { name: name.trim(), href: currentHref },
      ...views.filter((view) => view.name !== name.trim()),
    ].slice(0, 12);
    writeViews(storageKey, next);
    setViews(next);
  }

  function deleteView(name: string) {
    const next = views.filter((view) => view.name !== name);
    writeViews(storageKey, next);
    setViews(next);
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button type="button" variant="outline">
          <Bookmark className="mr-2 h-4 w-4" />
          Saved views
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-72">
        <DropdownMenuLabel>Saved views</DropdownMenuLabel>
        <DropdownMenuItem onSelect={saveCurrentView}>
          <Bookmark className="h-4 w-4" />
          Save current filters
        </DropdownMenuItem>
        {views.length > 0 ? <DropdownMenuSeparator /> : null}
        {views.map((view) => (
          <DropdownMenuItem
            key={`${view.name}:${view.href}`}
            onSelect={(event) => {
              event.preventDefault();
              router.push(view.href);
            }}
            className="group"
          >
            <span className="min-w-0 flex-1 truncate">{view.name}</span>
            <button
              type="button"
              aria-label={`Delete ${view.name}`}
              className="rounded-sm p-1 text-muted-foreground opacity-0 transition hover:bg-destructive/10 hover:text-destructive group-hover:opacity-100"
              onClick={(event) => {
                event.preventDefault();
                event.stopPropagation();
                deleteView(view.name);
              }}
            >
              <Trash2 className="h-3.5 w-3.5" />
            </button>
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function readViews(storageKey: string): SavedView[] {
  try {
    const raw = window.localStorage.getItem(storageKey);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as SavedView[];
    return Array.isArray(parsed)
      ? parsed.filter((view) => view.name && view.href)
      : [];
  } catch {
    return [];
  }
}

function writeViews(storageKey: string, views: SavedView[]) {
  window.localStorage.setItem(storageKey, JSON.stringify(views));
}
