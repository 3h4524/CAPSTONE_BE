import { randomUUID } from "node:crypto";
import { setTimeout as delay } from "node:timers/promises";
import { createClient } from "redis";
import { z } from "zod";

const eventSchema = z.object({
  schemaVersion: z.literal("1"), eventId: z.string().uuid(), jobId: z.string().uuid(),
  kind: z.enum(["render", "export_zip"]), queueVersion: z.coerce.number().int().positive().safe(),
}).strict();
export type MediaStreamEvent = z.infer<typeof eventSchema>;
type Entry = { id: string; values: Record<string, string> };

function entry(value: unknown): Entry | null {
  if (!Array.isArray(value) || typeof value[0] !== "string" || !Array.isArray(value[1])) return null;
  const fields = value[1] as unknown[];
  if (fields.length % 2 !== 0) return null;
  const values: Record<string, string> = {};
  for (let i = 0; i < fields.length; i += 2) {
    if (typeof fields[i] !== "string" || typeof fields[i + 1] !== "string") return { id: value[0], values: { invalid: "invalid" } };
    if (Object.hasOwn(values, fields[i] as string)) return { id: value[0], values: { invalid: "invalid" } };
    values[fields[i] as string] = fields[i + 1] as string;
  }
  return { id: value[0], values };
}

/** Wait for stream events; true means the API durably settled or rejected the job. */
export async function runMediaStream(signal: AbortSignal, handle: (event: MediaStreamEvent) => Promise<boolean>): Promise<void> {
  const url = process.env.MEDIA_QUEUE_REDIS_URL;
  const stream = process.env.MEDIA_QUEUE_STREAM;
  if (!url || !stream) throw new Error("MEDIA_QUEUE_REDIS_URL and MEDIA_QUEUE_STREAM are required in streams mode");
  const group = "media-workers", consumer = `worker-${randomUUID()}`;
  while (!signal.aborted) {
    const control = createClient({ url });
    const blocking = control.duplicate();
    control.on("error", () => {}); blocking.on("error", () => {});
    try {
      await control.connect();
      await blocking.connect();
      try { await control.sendCommand(["XGROUP", "CREATE", stream, group, "0", "MKSTREAM"]); }
      catch (error) { if (!(error instanceof Error) || !error.message.includes("BUSYGROUP")) throw error; }
      let nextRecovery = 0, cursor = "0-0";
      while (!signal.aborted) {
        let message: Entry | null = null;
        if (Date.now() >= nextRecovery) {
          const recovered = await control.sendCommand(["XAUTOCLAIM", stream, group, consumer, "130000", cursor, "COUNT", "1"]) as unknown;
          if (Array.isArray(recovered) && typeof recovered[0] === "string" && Array.isArray(recovered[1])) {
            cursor = recovered[0];
            message = entry(recovered[1][0]);
          }
          nextRecovery = cursor === "0-0" ? Date.now() + 30_000 : Date.now();
        }
        if (!message) {
          const response = await blocking.sendCommand(["XREADGROUP", "GROUP", group, consumer, "COUNT", "1", "BLOCK", "5000", "STREAMS", stream, ">"] ) as unknown;
          if (Array.isArray(response) && Array.isArray(response[0]) && Array.isArray(response[0][1])) message = entry(response[0][1][0]);
        }
        if (!message) continue;
        const parsed = eventSchema.safeParse(message.values);
        if (!parsed.success) {
          await control.sendCommand(["EVAL", "local t=redis.call('TYPE',KEYS[2]).ok; if t~='none' and t~='stream' then return redis.error_reply('invalid dead stream') end; redis.call('XADD',KEYS[2],'*','originalId',ARGV[2],'reason','invalid_message'); if redis.call('XACK',KEYS[1],ARGV[1],ARGV[2]) == 1 then redis.call('XDEL',KEYS[1],ARGV[2]) end; return 1", "2", stream, `${stream}:dead`, group, message.id]);
          continue;
        }
        if (await handle(parsed.data))
          await control.sendCommand(["EVAL", "if redis.call('XACK',KEYS[1],ARGV[1],ARGV[2]) == 1 then return redis.call('XDEL',KEYS[1],ARGV[2]) end; return 0", "1", stream, group, message.id]);
      }
    } catch (error) {
      if (!signal.aborted) {
        process.stderr.write("Media stream unavailable; reconnecting.\n");
        await delay(5000 + Math.floor(Math.random() * 5000), undefined, { signal }).catch(() => {});
      }
    } finally {
      if (blocking.isOpen) blocking.disconnect();
      if (control.isOpen) control.disconnect();
    }
  }
}
