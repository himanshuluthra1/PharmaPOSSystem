"use client";

export function SimpleBarChart({
  labels,
  series,
  height = 180,
}: {
  labels: string[];
  series: { name: string; values: number[]; color: string }[];
  height?: number;
}) {
  const max = Math.max(1, ...series.flatMap((s) => s.values));
  return (
    <div className="w-full" style={{ height }}>
      <div className="flex h-full items-end gap-1">
        {labels.map((label, i) => (
          <div key={label + i} className="flex min-w-0 flex-1 flex-col items-center gap-1">
            <div className="flex h-full w-full items-end justify-center gap-0.5">
              {series.map((s) => {
                const v = s.values[i] || 0;
                const h = Math.round((v / max) * 100);
                return (
                  <div
                    key={s.name}
                    title={`${s.name}: ${v}`}
                    className="w-full max-w-[14px] rounded-t"
                    style={{ height: `${Math.max(h, v > 0 ? 2 : 0)}%`, background: s.color }}
                  />
                );
              })}
            </div>
            <div className="w-full truncate text-center text-[10px] text-slate-500">
              {label.length > 7 ? label.slice(5) : label}
            </div>
          </div>
        ))}
      </div>
      <div className="mt-2 flex flex-wrap gap-3 text-xs text-slate-500">
        {series.map((s) => (
          <span key={s.name} className="inline-flex items-center gap-1">
            <span className="inline-block h-2 w-2 rounded-full" style={{ background: s.color }} />
            {s.name}
          </span>
        ))}
      </div>
    </div>
  );
}

export function SimpleDonut({
  slices,
}: {
  slices: { label: string; value: number; color: string }[];
}) {
  const total = slices.reduce((a, s) => a + s.value, 0) || 1;
  let acc = 0;
  const gradient = slices
    .map((s) => {
      const start = (acc / total) * 360;
      acc += s.value;
      const end = (acc / total) * 360;
      return `${s.color} ${start}deg ${end}deg`;
    })
    .join(", ");

  return (
    <div className="flex flex-wrap items-center gap-4">
      <div
        className="h-36 w-36 rounded-full"
        style={{
          background: `conic-gradient(${gradient || "#e2e8f0 0deg 360deg"})`,
          mask: "radial-gradient(circle at center, transparent 45%, black 46%)",
          WebkitMask: "radial-gradient(circle at center, transparent 45%, black 46%)",
        }}
      />
      <ul className="space-y-1 text-sm">
        {slices.map((s) => (
          <li key={s.label} className="flex items-center gap-2">
            <span className="h-2.5 w-2.5 rounded-full" style={{ background: s.color }} />
            <span className="text-slate-600">{s.label}</span>
            <span className="font-medium text-slate-800">
              {Math.round((s.value / total) * 100)}%
            </span>
          </li>
        ))}
        {slices.length === 0 ? (
          <li className="text-slate-500">No data</li>
        ) : null}
      </ul>
    </div>
  );
}
