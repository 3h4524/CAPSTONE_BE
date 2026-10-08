import { spawn } from "node:child_process";

export async function processOutput(command: string, args: string[], signal: AbortSignal): Promise<string> {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { signal, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
    let out = "";
    child.stdout.on("data", data => { out += String(data); if (out.length > 2_000_000) child.kill(); });
    child.stderr.on("data", () => {});
    child.once("error", reject);
    child.once("close", code => code === 0 ? resolve(out) : reject(new Error(`Media process failed (${code})`)));
  });
}
