import { stat } from "node:fs/promises";
import { z } from "zod";
import { processOutput } from "./process";
import type { MediaQa } from "./contracts";

const probeSchema = z.object({ streams: z.array(z.object({ codec_type: z.string(), codec_name: z.string().optional(), pix_fmt: z.string().optional(), width: z.number().optional(), height: z.number().optional(), avg_frame_rate: z.string().optional() })), format: z.object({ duration: z.string() }) });
export function validQa(qa: MediaQa, duration: number, width = 1080, height = 2160) {
  return qa.codec === "h264" && qa.pixelFormat === "yuv420p" && qa.width === width && qa.height === height && Math.abs(qa.fps - 30) < .01 && Math.abs(qa.durationSeconds - duration) <= 1 / 30 + .01 && qa.bytes > 0 && qa.bytes < 100 * 1024 * 1024 && !qa.hasAudio && qa.fullDecode;
}
export async function inspectVideo(file: string, duration: number, signal: AbortSignal, width = 1080, height = 2160): Promise<MediaQa> {
  const probe = probeSchema.parse(JSON.parse(await processOutput(process.env.FFPROBE_PATH ?? "ffprobe", ["-v", "error", "-show_streams", "-show_format", "-of", "json", file], signal)));
  const stream = probe.streams.find(s => s.codec_type === "video");
  if (!stream) throw new Error("No video stream");
  const [a = "0", b = "1"] = (stream.avg_frame_rate ?? "0/1").split("/");
  await processOutput(process.env.FFMPEG_PATH ?? "ffmpeg", ["-v", "error", "-xerror", "-i", file, "-f", "null", "-"], signal);
  const qa = { codec: stream.codec_name ?? "", pixelFormat: stream.pix_fmt ?? "", width: stream.width ?? 0, height: stream.height ?? 0, fps: Number(a) / Number(b), durationSeconds: Number(probe.format.duration), bytes: (await stat(file)).size, hasAudio: probe.streams.some(s => s.codec_type === "audio"), fullDecode: true };
  if (!validQa(qa, duration, width, height)) throw new Error("Technical QA failed");
  return qa;
}
