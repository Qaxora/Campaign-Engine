"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { BarChart3, Table2 } from "lucide-react";
import { api } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import { formatAmount, formatCompact, formatDate, formatInteger, toNumber } from "@/lib/format";
import { problemMessage } from "@/lib/problem";
import { cn } from "@/lib/utils";

type TrendPoint = components["schemas"]["TrendPoint"];
type Metric = "discount" | "redemptions";

const HEIGHT = 220;
const PAD = { top: 12, right: 8, bottom: 28, left: 44 };

/**
 * Daily discount or redemptions for the period. Days are cut in the viewer's time zone, which the
 * API applies (the browser only reports it). One series, one axis: the two measures are switched,
 * never overlaid on a second scale.
 */
export function TrendChart({ from, to }: { from: string; to: string }) {
  const [points, setPoints] = useState<TrendPoint[]>();
  const [timeZone, setTimeZone] = useState<string>();
  const [error, setError] = useState<string>();
  const [metric, setMetric] = useState<Metric>("discount");
  const [view, setView] = useState<"chart" | "table">("chart");

  useEffect(() => {
    let cancelled = false;
    const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    api.GET("/api/v1/analytics/trends", { params: { query: { from, to, timeZone: zone } } }).then(({ data, error: problem, response }) => {
      if (cancelled) return;
      if (!data) {
        setError(problemMessage(problem, response.status));
        return;
      }

      setPoints(data.points);
      setTimeZone(data.timeZone);
    });
    return () => {
      cancelled = true;
    };
  }, [from, to]);

  const values = useMemo(() => (points ?? []).map((p) => toNumber(metric === "discount" ? p.discount : p.redemptions)), [points, metric]);
  const empty = points !== undefined && values.every((v) => v === 0);
  const format = metric === "discount" ? formatAmount : formatInteger;

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-3 px-5 pt-4">
        <div role="radiogroup" aria-label="Measure" className="inline-flex rounded-md border border-border p-0.5 text-sm">
          {(["discount", "redemptions"] as const).map((m) => (
            <button
              key={m}
              role="radio"
              aria-checked={metric === m}
              onClick={() => setMetric(m)}
              className={cn("rounded px-3 py-1 capitalize", metric === m ? "bg-surface-muted font-medium text-foreground" : "text-muted hover:text-foreground")}
            >
              {m}
            </button>
          ))}
        </div>
        <button
          onClick={() => setView(view === "chart" ? "table" : "chart")}
          className="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-sm text-muted hover:bg-surface-muted hover:text-foreground"
        >
          {view === "chart" ? <Table2 className="size-4" aria-hidden /> : <BarChart3 className="size-4" aria-hidden />}
          {view === "chart" ? "Show table" : "Show chart"}
        </button>
      </div>

      <div className="px-5 pb-5 pt-3">
        {error ? (
          <p className="py-16 text-center text-sm text-danger">{error}</p>
        ) : points === undefined ? (
          <div className="animate-pulse rounded-md bg-surface-muted" style={{ height: HEIGHT }} aria-label="Loading trend" />
        ) : empty ? (
          <p className="flex items-center justify-center text-sm text-muted" style={{ height: HEIGHT }}>
            No redemptions in this period yet.
          </p>
        ) : view === "table" ? (
          <TrendTable points={points} />
        ) : (
          <Bars points={points} values={values} format={format} label={metric} />
        )}
        {timeZone && points && !empty ? <p className="mt-2 text-xs text-muted">Days in {timeZone}. Reversed sales excluded.</p> : null}
      </div>
    </div>
  );
}

function Bars({ points, values, format, label }: { points: TrendPoint[]; values: number[]; format: (v: number) => string; label: string }) {
  const ref = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);
  const [hover, setHover] = useState<number>();

  useEffect(() => {
    const element = ref.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) => setWidth(entry!.contentRect.width));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const max = niceMax(Math.max(...values));
  const plotWidth = Math.max(0, width - PAD.left - PAD.right);
  const plotHeight = HEIGHT - PAD.top - PAD.bottom;
  const step = values.length > 0 ? plotWidth / values.length : 0;
  const barWidth = Math.max(2, Math.min(28, step - 2)); // ≥2px surface gap between bars
  const y = (v: number) => PAD.top + plotHeight - (max === 0 ? 0 : (v / max) * plotHeight);
  const ticks = [0, max / 2, max];
  const labelEvery = Math.ceil(values.length / Math.max(1, Math.floor(plotWidth / 72)));
  const total = values.reduce((a, b) => a + b, 0);

  return (
    <div ref={ref} className="relative" onMouseLeave={() => setHover(undefined)}>
      {width > 0 ? (
        <svg width={width} height={HEIGHT} role="img" aria-label={`Daily ${label}: ${format(total)} in total over ${values.length} days`}>
          {ticks.map((t) => (
            <g key={t}>
              <line x1={PAD.left} x2={width - PAD.right} y1={y(t)} y2={y(t)} className="stroke-chart-grid" strokeWidth={1} />
              <text x={PAD.left - 8} y={y(t)} dy="0.32em" textAnchor="end" className="fill-muted text-[11px] tabular-nums">
                {formatCompact(t)}
              </text>
            </g>
          ))}
          {values.map((v, i) => {
            const x = PAD.left + i * step + (step - barWidth) / 2;
            const h = Math.max(0, y(0) - y(v));
            return (
              <g key={points[i]!.date}>
                {h > 0 ? <path d={roundedTop(x, y(v), barWidth, h, Math.min(4, barWidth / 2))} className={cn("fill-chart-1", hover !== undefined && hover !== i && "opacity-40")} /> : null}
                {/* Hit target: the whole column, larger than the bar. */}
                <rect x={PAD.left + i * step} y={PAD.top} width={step} height={plotHeight} fill="transparent" onMouseEnter={() => setHover(i)} />
                {i % labelEvery === 0 ? (
                  <text x={x + barWidth / 2} y={HEIGHT - 8} textAnchor="middle" className="fill-muted text-[11px]">
                    {formatDate(points[i]!.date, false)}
                  </text>
                ) : null}
              </g>
            );
          })}
        </svg>
      ) : (
        <div style={{ height: HEIGHT }} />
      )}
      {hover !== undefined && width > 0 ? (
        <div
          role="tooltip"
          className="pointer-events-none absolute top-0 z-10 -translate-x-1/2 rounded-md border border-border bg-surface px-3 py-2 text-xs shadow-md"
          style={{ left: Math.min(Math.max(PAD.left + hover * step + step / 2, 70), width - 70) }}
        >
          <p className="font-medium text-foreground">{formatDate(points[hover]!.date)}</p>
          <p className="mt-0.5 text-muted">
            Discount <span className="font-medium tabular-nums text-foreground">{formatAmount(points[hover]!.discount)}</span>
          </p>
          <p className="text-muted">
            Redemptions <span className="font-medium tabular-nums text-foreground">{formatInteger(points[hover]!.redemptions)}</span>
          </p>
          <p className="text-muted">
            Sales <span className="font-medium tabular-nums text-foreground">{formatInteger(points[hover]!.transactions)}</span>
          </p>
        </div>
      ) : null}
    </div>
  );
}

function TrendTable({ points }: { points: TrendPoint[] }) {
  return (
    <div className="max-h-[220px] overflow-y-auto">
      <table className="w-full text-sm">
        <thead className="sticky top-0 bg-surface text-left text-xs text-muted">
          <tr>
            <th className="py-1.5 font-medium">Day</th>
            <th className="py-1.5 text-right font-medium">Sales</th>
            <th className="py-1.5 text-right font-medium">Redemptions</th>
            <th className="py-1.5 text-right font-medium">Discount</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border tabular-nums">
          {points.map((p) => (
            <tr key={p.date}>
              <td className="py-1.5">{formatDate(p.date)}</td>
              <td className="py-1.5 text-right">{formatInteger(p.transactions)}</td>
              <td className="py-1.5 text-right">{formatInteger(p.redemptions)}</td>
              <td className="py-1.5 text-right">{formatAmount(p.discount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Rounds the axis maximum up to 1, 2 or 5 × 10ⁿ so ticks are readable. */
export function niceMax(value: number): number {
  if (value <= 0) return 0;
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const step = [1, 2, 5, 10].find((s) => s * magnitude >= value)!;
  return step * magnitude;
}

/** A bar with rounded top corners, anchored flat on the baseline. */
function roundedTop(x: number, top: number, width: number, height: number, radius: number): string {
  const r = Math.min(radius, height);
  const bottom = top + height;
  return `M${x},${bottom}V${top + r}Q${x},${top} ${x + r},${top}H${x + width - r}Q${x + width},${top} ${x + width},${top + r}V${bottom}Z`;
}
