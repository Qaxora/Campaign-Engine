/**
 * The builder edits start/end as wall-clock times in the campaign's time zone ("2026-10-01T00:00" in
 * Europe/Istanbul); the API stores instants with an offset. These helpers only convert between the two.
 */

/** Offset of `zone` at `instant`, in minutes east of UTC. */
function offsetMinutes(instant: Date, zone: string): number {
  const parts = new Intl.DateTimeFormat("en-US", {
    timeZone: zone,
    hourCycle: "h23",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  }).formatToParts(instant);
  const get = (type: string) => Number(parts.find((p) => p.type === type)!.value);
  const asUtc = Date.UTC(get("year"), get("month") - 1, get("day"), get("hour"), get("minute"), get("second"));
  return Math.round((asUtc - instant.getTime()) / 60000);
}

function formatOffset(minutes: number): string {
  const sign = minutes >= 0 ? "+" : "-";
  const abs = Math.abs(minutes);
  return `${sign}${String(Math.floor(abs / 60)).padStart(2, "0")}:${String(abs % 60).padStart(2, "0")}`;
}

/** "2026-10-01T00:00" in Europe/Istanbul → "2026-10-01T00:00:00+03:00". Empty stays empty. */
export function zonedToIso(local: string, zone: string): string | null {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(local)) return null;
  const naiveUtc = Date.parse(`${local}:00Z`);
  // Two passes settle the offset around daylight-saving changes.
  let offset = offsetMinutes(new Date(naiveUtc), zone);
  offset = offsetMinutes(new Date(naiveUtc - offset * 60000), zone);
  return `${local}:00${formatOffset(offset)}`;
}

/** "2026-09-30T21:00:00Z" shown in Europe/Istanbul → "2026-10-01T00:00" (for datetime-local inputs). */
export function isoToZoned(iso: string | null | undefined, zone: string): string {
  if (!iso) return "";
  const instant = new Date(iso);
  if (Number.isNaN(instant.getTime())) return "";
  const shifted = new Date(instant.getTime() + offsetMinutes(instant, zone) * 60000);
  return shifted.toISOString().slice(0, 16);
}

export function isValidTimeZone(zone: string): boolean {
  try {
    new Intl.DateTimeFormat("en-US", { timeZone: zone });
    return true;
  } catch {
    return false;
  }
}
