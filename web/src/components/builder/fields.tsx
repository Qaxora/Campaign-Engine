"use client";

import { useId, useState } from "react";
import { X } from "lucide-react";
import { Input, Label } from "@/components/ui/form";
import { cn } from "@/lib/utils";

export function Section({ id, title, description, children }: { id: string; title: string; description?: string; children: React.ReactNode }) {
  return (
    <section id={id} aria-labelledby={`${id}-title`} className="scroll-mt-20 rounded-lg border border-border bg-surface">
      <div className="border-b border-border px-5 py-4">
        <h2 id={`${id}-title`} className="text-sm font-semibold">
          {title}
        </h2>
        {description ? <p className="mt-0.5 text-sm text-muted">{description}</p> : null}
      </div>
      <div className="grid gap-4 px-5 py-4 sm:grid-cols-2">{children}</div>
    </section>
  );
}

export function TextField({
  label,
  value,
  onChange,
  hint,
  placeholder,
  wide,
  type = "text",
  inputMode,
  mono,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  hint?: string;
  placeholder?: string;
  wide?: boolean;
  type?: string;
  inputMode?: "decimal" | "numeric" | "text";
  mono?: boolean;
}) {
  const id = useId();
  return (
    <div className={cn("flex flex-col gap-1.5", wide && "sm:col-span-2")}>
      <Label htmlFor={id}>{label}</Label>
      <Input id={id} type={type} inputMode={inputMode} value={value} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} className={mono ? "font-mono" : undefined} />
      {hint ? <p className="text-xs text-muted">{hint}</p> : null}
    </div>
  );
}

export function NumberField(props: Omit<Parameters<typeof TextField>[0], "inputMode" | "type">) {
  return <TextField {...props} inputMode="decimal" />;
}

export function SelectField<T extends string>({
  label,
  value,
  options,
  onChange,
  hint,
  wide,
}: {
  label: string;
  value: T;
  options: readonly { value: T; label: string }[];
  onChange: (value: T) => void;
  hint?: string;
  wide?: boolean;
}) {
  const id = useId();
  return (
    <div className={cn("flex flex-col gap-1.5", wide && "sm:col-span-2")}>
      <Label htmlFor={id}>{label}</Label>
      <select id={id} value={value} onChange={(e) => onChange(e.target.value as T)} className="h-10 rounded-md border border-border bg-surface px-2 text-sm">
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
      {hint ? <p className="text-xs text-muted">{hint}</p> : null}
    </div>
  );
}

export function CheckboxField({ label, checked, onChange, hint, wide }: { label: string; checked: boolean; onChange: (value: boolean) => void; hint?: string; wide?: boolean }) {
  const id = useId();
  return (
    <div className={cn("flex items-start gap-2", wide && "sm:col-span-2")}>
      <input id={id} type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} className="mt-1" />
      <div>
        <label htmlFor={id} className="text-sm font-medium">
          {label}
        </label>
        {hint ? <p className="text-xs text-muted">{hint}</p> : null}
      </div>
    </div>
  );
}

/**
 * A list of values typed or picked from suggestions (catalog categories, brands, stores …). Values
 * outside the suggestions are allowed: carts may send codes the catalog does not know yet.
 */
export function ChipInput({
  label,
  values,
  onChange,
  suggestions = [],
  placeholder,
  hint,
  wide,
  upper,
}: {
  label: string;
  values: string[];
  onChange: (values: string[]) => void;
  suggestions?: readonly { value: string; label?: string }[];
  placeholder?: string;
  hint?: string;
  wide?: boolean;
  upper?: boolean;
}) {
  const id = useId();
  const [draft, setDraft] = useState("");

  function add(raw: string) {
    const parts = raw
      .split(/[,\n]/)
      .map((p) => (upper ? p.trim().toUpperCase() : p.trim()))
      .filter(Boolean);
    const next = [...values];
    for (const part of parts) if (!next.some((v) => v.toLowerCase() === part.toLowerCase())) next.push(part);
    onChange(next);
    setDraft("");
  }

  return (
    <div className={cn("flex flex-col gap-1.5", wide && "sm:col-span-2")}>
      <Label htmlFor={id}>{label}</Label>
      <div className="flex min-h-10 flex-wrap items-center gap-1.5 rounded-md border border-border bg-surface px-2 py-1.5 focus-within:border-brand focus-within:ring-4 focus-within:ring-ring">
        {values.map((value) => (
          <span key={value} className="inline-flex items-center gap-1 rounded bg-surface-muted px-2 py-0.5 text-sm">
            {suggestions.find((s) => s.value === value)?.label ?? value}
            <button type="button" aria-label={`Remove ${value}`} onClick={() => onChange(values.filter((v) => v !== value))} className="text-muted hover:text-foreground">
              <X className="size-3" />
            </button>
          </span>
        ))}
        <input
          id={id}
          list={suggestions.length > 0 ? `${id}-options` : undefined}
          value={draft}
          placeholder={values.length === 0 ? placeholder : undefined}
          onChange={(e) => {
            const value = e.target.value;
            // Picking from the datalist fills the whole value at once.
            if (suggestions.some((s) => s.value === value)) add(value);
            else setDraft(value);
          }}
          onKeyDown={(e) => {
            if ((e.key === "Enter" || e.key === ",") && draft.trim()) {
              e.preventDefault();
              add(draft);
            } else if (e.key === "Backspace" && draft === "" && values.length > 0) {
              onChange(values.slice(0, -1));
            }
          }}
          onBlur={() => draft.trim() && add(draft)}
          className="min-w-[8rem] flex-1 bg-transparent py-0.5 text-sm outline-none placeholder:text-muted"
        />
        {suggestions.length > 0 ? (
          <datalist id={`${id}-options`}>
            {suggestions
              .filter((s) => !values.includes(s.value))
              .map((s) => (
                <option key={s.value} value={s.value}>
                  {s.label}
                </option>
              ))}
          </datalist>
        ) : null}
      </div>
      {hint ? <p className="text-xs text-muted">{hint}</p> : null}
    </div>
  );
}
