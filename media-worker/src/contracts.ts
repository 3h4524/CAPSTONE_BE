import { z } from "zod";

const region = z.object({ x: z.number().min(0).max(1), y: z.number().min(0).max(1), width: z.number().positive().max(1), height: z.number().positive().max(1) });
export const sceneSchema = z.object({ mockupId: z.string().uuid(), sourceRevision: z.number().int().positive(), sourceHash: z.string().length(64), role: z.string(), sceneOrder: z.number().int().nonnegative(), durationFrames: z.number().int().positive(), crop: region, endCrop: region, motion: z.enum(["gentle", "contain_gentle", "static", "pan", "zoom", "zoom_out", "pan_left", "pan_up", "pan_down", "diagonal_up_right", "diagonal_up_left", "diagonal_down_right", "diagonal_down_left"]), transition: z.enum(["fade", "cut"]), text: z.string().max(120), generationStrategy: z.literal("standard"), warnings: z.array(z.string()) });
const outputFormats = {
  square: { width: 1080, height: 1080, aspectRatio: "1:1" },
  portrait: { width: 1080, height: 1920, aspectRatio: "9:16" },
  tall: { width: 1080, height: 2160, aspectRatio: "1:2" },
  landscape: { width: 1920, height: 1080, aspectRatio: "16:9" },
} as const;
export const storyboardSchema = z.object({ template: z.enum(["product_showcase", "design_detail", "variant_showcase"]), templateVersion: z.union([z.literal(1), z.literal(2)]), durationSeconds: z.number().int().min(3).max(15), productType: z.string(), fingerprint: z.string().length(64), outputFormat: z.enum(["square", "portrait", "tall", "landscape"]).default("tall"), width: z.number().int().positive().default(1080), height: z.number().int().positive().default(2160), aspectRatio: z.string().default("1:2"), scenes: z.array(sceneSchema).min(2).max(4) })
  .refine(b => b.scenes.reduce((n, s) => n + s.durationFrames, 0) === b.durationSeconds * 30, "Scene duration mismatch")
  .refine(b => { const expected = outputFormats[b.outputFormat]; return b.width === expected.width && b.height === expected.height && b.aspectRatio === expected.aspectRatio; }, "Output format dimensions do not match");
export const uploadSchema = z.object({ url: z.string().url(), fields: z.record(z.string(), z.string()), storageKey: z.string(), resourceType: z.enum(["image", "video", "raw"]) });
export const workerJobSchema = z.object({ id: z.string().uuid(), leaseToken: z.string().uuid(), kind: z.enum(["render", "export_zip"]), payload: z.unknown(), assets: z.array(z.object({ mockupId: z.string().uuid(), url: z.string().url(), fileName: z.string() })).max(10), uploads: z.record(z.string(), uploadSchema) });
export const renderPayloadSchema = z.object({ videoId: z.string().uuid(), storyboard: storyboardSchema });
export const exportPayloadSchema = z.object({ exportId: z.string().uuid(), videoId: z.string().uuid(), manifest: z.object({ schemaVersion: z.literal(1), productId: z.string().uuid(), videoId: z.string().uuid(), version: z.number().int(), mode: z.literal("standard"), fingerprint: z.string(), approvedRevision: z.number(), video: z.literal("video.mp4"), thumbnail: z.literal("thumbnail.png"), scenes: z.array(z.object({ order: z.number(), mockupId: z.string().uuid(), sourceRevision: z.number(), sourceHash: z.string().length(64), generationStrategy: z.literal("standard"), source: z.string() })) }).strict() });
export type Storyboard = z.infer<typeof storyboardSchema>;
export type Scene = z.infer<typeof sceneSchema>;
export type WorkerJob = z.infer<typeof workerJobSchema>;
export type Upload = z.infer<typeof uploadSchema>;
export type RenderProps = { storyboard: Storyboard; sources: Record<string, { url: string; width: number; height: number }> };
export interface MediaQa { codec: string; pixelFormat: string; width: number; height: number; fps: number; durationSeconds: number; bytes: number; hasAudio: boolean; fullDecode: boolean }
