"use client";

import { ChevronDown } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { cn } from "@/lib/utils";

export interface MultiSelectFilterOption {
  value: string;
  label: string;
}

interface MultiSelectFilterProps {
  id: string;
  name: string;
  allLabel: string;
  options: MultiSelectFilterOption[];
  value?: string;
  className?: string;
}

export function MultiSelectFilter({
  id,
  name,
  allLabel,
  options,
  value,
  className,
}: MultiSelectFilterProps) {
  const [selected, setSelected] = useState(() => parseValue(value));
  const selectedSet = new Set(selected);
  const selectedOptions = options.filter((option) => selectedSet.has(option.value));
  const hiddenValue = selected.join(",");
  const triggerLabel =
    selectedOptions.length === 0
      ? selected.length === 0
        ? allLabel
        : `${selected.length} seleccionados`
      : selectedOptions.length === 1
        ? selectedOptions[0].label
        : `${selectedOptions.length} seleccionados`;

  function toggle(value: string) {
    const nextSet = new Set(selected);
    if (nextSet.has(value)) {
      nextSet.delete(value);
    } else {
      nextSet.add(value);
    }
    setSelected(options.filter((option) => nextSet.has(option.value)).map((option) => option.value));
  }

  return (
    <>
      <input id={`${id}-value`} type="hidden" name={name} value={hiddenValue} readOnly />
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button
            id={id}
            type="button"
            variant="outline"
            className={cn("h-10 min-w-40 justify-between font-normal", className)}
          >
            <span className="truncate">{triggerLabel}</span>
            <ChevronDown className="ml-2 h-4 w-4 opacity-50" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="start" className="max-h-72 min-w-52 overflow-y-auto">
          {options.map((option) => (
            <DropdownMenuCheckboxItem
              key={option.value}
              checked={selectedSet.has(option.value)}
              onCheckedChange={() => toggle(option.value)}
              onSelect={(event) => event.preventDefault()}
            >
              {option.label}
            </DropdownMenuCheckboxItem>
          ))}
        </DropdownMenuContent>
      </DropdownMenu>
    </>
  );
}

function parseValue(value: string | undefined) {
  return (value ?? "")
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);
}
