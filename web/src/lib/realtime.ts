type RealtimeEvent = { storeId: string; entityType: string; localId: number };
type Listener = (event: RealtimeEvent) => void;

type RealtimeBus = {
  listeners: Set<Listener>;
};

const GLOBAL_KEY = "__pharmapos_realtime_bus__";

function getBus(): RealtimeBus {
  const g = globalThis as typeof globalThis & {
    [GLOBAL_KEY]?: RealtimeBus;
  };
  if (!g[GLOBAL_KEY]) {
    g[GLOBAL_KEY] = { listeners: new Set<Listener>() };
  }
  return g[GLOBAL_KEY];
}

export function subscribeRealtime(listener: Listener) {
  const bus = getBus();
  bus.listeners.add(listener);
  return () => bus.listeners.delete(listener);
}

export function publishRealtime(event: RealtimeEvent) {
  for (const listener of getBus().listeners) {
    try {
      listener(event);
    } catch {
      // ignore listener errors
    }
  }
}
