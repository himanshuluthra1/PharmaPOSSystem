type Listener = (event: { storeId: string; entityType: string; localId: number }) => void;

const listeners = new Set<Listener>();

export function subscribeRealtime(listener: Listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function publishRealtime(event: {
  storeId: string;
  entityType: string;
  localId: number;
}) {
  for (const listener of listeners) {
    try {
      listener(event);
    } catch {
      // ignore listener errors
    }
  }
}
