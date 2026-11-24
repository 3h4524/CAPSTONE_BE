/**
 * Runs `task` for every item with at most `limit` running at once, and returns the results in the order of the items. After
 * one fails nothing new is started; the ones already running are waited for (so nothing is still writing to a directory
 * the caller is about to remove), and then the first error is thrown.
 */
export async function mapWithConcurrency<T, R>(items: readonly T[], limit: number,
  task: (item: T, index: number) => Promise<R>): Promise<R[]> {
  const results = new Array<R>(items.length);
  const errors: unknown[] = [];
  let next = 0;
  const worker = async () => {
    while (errors.length === 0) {
      const index = next++;
      if (index >= items.length) return;
      try { results[index] = await task(items[index]!, index); }
      catch (error) { errors.push(error); return; }
    }
  };
  await Promise.all(Array.from({ length: Math.max(1, Math.min(Math.floor(limit), items.length)) }, worker));
  if (errors.length > 0) throw errors[0];
  return results;
}

/**
 * How many scenes of one export are rendered at the same time. Each scene is its own browser and uses `tabsPerScene` tabs,
 * which is what limits the machine, so by default as many as fit side by side in its cores, at most two (a host with two
 * cores keeps rendering one at a time, as it always did). EXPORT_SCENE_CONCURRENCY (1-4) sets it for a bigger host.
 */
export function readExportSceneConcurrency(value: string | undefined, cores: number, tabsPerScene: number) {
  const configured = Number(value);
  if (value?.trim() && Number.isInteger(configured) && configured >= 1) return Math.min(configured, 4);
  return Math.max(1, Math.min(2, Math.floor(cores / Math.max(1, tabsPerScene))));
}
