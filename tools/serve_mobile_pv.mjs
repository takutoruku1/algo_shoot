import { createReadStream } from 'node:fs';
import { copyFile, mkdir, open, readFile, readdir, realpath, rename, stat, unlink, writeFile } from 'node:fs/promises';
import { createHash, randomBytes, randomUUID, timingSafeEqual } from 'node:crypto';
import { createServer } from 'node:http';
import { basename, dirname, extname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import { pipeline } from 'node:stream/promises';
import { parseArgs } from 'node:util';

const script = fileURLToPath(import.meta.url);
const project = resolve(dirname(script), '..');
export const defaultRoot = join(project, 'output', 'mobile_evidence');
const imageTypes = { '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.webp': 'image/webp', '.gif': 'image/gif' };
const videoTypes = new Set(['.mp4', '.avi', '.mov', '.mkv', '.webm']);
const cookieName = 'refrain_evidence';
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

async function initialize(root) {
  await mkdir(join(root, 'media'), { recursive: true });
  await mkdir(join(root, 'entries'), { recursive: true });
  await writeFile(join(root, '.gdignore'), '');
}

async function atomicJson(path, data) {
  const temporary = `${path}.${randomUUID()}.tmp`;
  await writeFile(temporary, JSON.stringify(data, null, 2), { mode: 0o600 });
  await rename(temporary, path);
}

async function readJson(path) {
  try { return JSON.parse(await readFile(path, 'utf8')); }
  catch (error) { if (error.code === 'ENOENT') return null; throw error; }
}

async function hashFile(path) {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(path)) hash.update(chunk);
  return hash.digest('hex');
}

async function run(command, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    let output = '', errors = '';
    child.stdout.on('data', chunk => { output += chunk; });
    child.stderr.on('data', chunk => { errors = (errors + chunk).slice(-8000); });
    child.on('error', reject);
    child.on('close', code => code === 0 ? resolve(output) : reject(new Error(`${command} failed (${code}): ${errors}`)));
  });
}

export async function addEvidence({ files, title, note = '', root = defaultRoot }) {
  if (!files.length) throw new Error('Specify at least one image or video file.');
  await initialize(root);
  const sources = await Promise.all(files.map(async file => {
    const path = await realpath(resolve(file));
    const extension = extname(path).toLowerCase();
    if (!imageTypes[extension] && !videoTypes.has(extension)) throw new Error(`Unsupported media type: ${file}`);
    const info = await stat(path);
    if (!info.isFile() || info.size === 0) throw new Error(`Not a non-empty media file: ${file}`);
    return { path, extension, info };
  }));
  const entries = [];
  for (const { path, extension, info } of sources) {
    const id = await hashFile(path);
    const video = videoTypes.has(extension);
    const storedName = id + (video ? '.mp4' : extension);
    const destination = join(root, 'media', storedName);
    const temporary = join(root, 'media', `${randomUUID()}.tmp${video ? '.mp4' : extension}`);
    if (video) {
      const probe = JSON.parse(await run('ffprobe', ['-v', 'error', '-show_streams', '-of', 'json', path]));
      const picture = probe.streams.find(stream => stream.codec_type === 'video');
      if (!picture) throw new Error(`No video stream: ${path}`);
      const compatible = picture.codec_name === 'h264' && picture.pix_fmt === 'yuv420p';
      const audio = probe.streams.find(stream => stream.codec_type === 'audio');
      await run('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-nostdin', '-i', path,
        '-map', '0:v:0', '-map', '0:a:0?', '-map_metadata', '-1',
        ...(compatible ? ['-c:v', 'copy'] : ['-c:v', 'libx264', '-preset', 'fast', '-crf', '20', '-pix_fmt', 'yuv420p', '-color_range', 'tv', '-vf', 'scale=in_range=auto:out_range=tv,pad=ceil(iw/2)*2:ceil(ih/2)*2']),
        '-c:a', audio?.codec_name === 'aac' ? 'copy' : 'aac', '-movflags', '+faststart', temporary]);
    } else {
      await copyFile(path, temporary);
    }
    await rename(temporary, destination);
    const entry = {
      id, title: title || basename(path), note, name: basename(path), storedName,
      type: video ? 'video' : 'image', mime: video ? 'video/mp4' : imageTypes[extension],
      size: (await stat(destination)).size, sourceSha256: id,
      sha256: await hashFile(destination), capturedAt: info.mtime.toISOString(), addedAt: new Date().toISOString(),
    };
    await atomicJson(join(root, 'entries', `${id}.json`), entry);
    entries.push(entry);
  }
  return entries;
}

async function getEntries(root) {
  const names = await readdir(join(root, 'entries'));
  const entries = await Promise.all(names.filter(name => /^[a-f0-9]{64}\.json$/.test(name)).map(name => readJson(join(root, 'entries', name))));
  return entries.sort((a, b) => b.addedAt.localeCompare(a.addedAt));
}

function secretMatches(actual, expected) {
  if (typeof actual !== 'string' || typeof expected !== 'string') return false;
  const a = Buffer.from(actual), b = Buffer.from(expected);
  return a.length === b.length && timingSafeEqual(a, b);
}

export function byteRange(header, size) {
  const match = /^bytes=(\d*)-(\d*)$/.exec(header);
  if (!match || (!match[1] && !match[2]) || size === 0) return null;
  const first = match[1] === '' ? null : Number(match[1]);
  const last = match[2] === '' ? null : Number(match[2]);
  if ((first !== null && !Number.isSafeInteger(first)) || (last !== null && !Number.isSafeInteger(last))) return null;
  const start = first === null ? Math.max(0, size - last) : first;
  const end = first === null || last === null ? size - 1 : Math.min(last, size - 1);
  return start < size && start <= end ? { start, end } : null;
}

export async function createEvidenceServer({ root = defaultRoot, token, controlToken, secure = true, onStop = () => {} }) {
  await initialize(root);
  const template = await readFile(join(dirname(script), 'evidence_gallery.html'), 'utf8');
  const mediaRoot = await realpath(join(root, 'media'));
  const server = createServer(async (req, res) => {
    const nonce = randomBytes(18).toString('base64');
    res.setHeader('Cache-Control', 'no-store');
    res.setHeader('Referrer-Policy', 'no-referrer');
    res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('X-Robots-Tag', 'noindex, nofollow, noarchive');
    res.setHeader('Content-Security-Policy', `default-src 'none'; script-src 'nonce-${nonce}'; style-src 'nonce-${nonce}'; img-src 'self'; media-src 'self'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'`);
    const reply = (code, content = '', type = 'text/plain; charset=utf-8') => {
      res.writeHead(code, { 'Content-Type': type });
      res.end(req.method === 'HEAD' ? undefined : content);
    };
    try {
      const url = new URL(req.url, 'http://localhost');
      const path = url.pathname;
      if (path.startsWith('/control/')) {
        if (req.headers.host !== `127.0.0.1:${server.address().port}` ||
            !secretMatches(req.headers['x-evidence-control'], controlToken)) return reply(404);
        if (path === '/control/status' && req.method === 'GET') return reply(200, 'ok');
        if (path === '/control/stop' && req.method === 'POST') {
          reply(200, 'stopping');
          setImmediate(onStop);
          return;
        }
        return reply(404);
      }
      if (path === '/session' && req.method === 'POST') {
        if (req.headers['content-type'] !== 'application/json') return reply(415);
        let body = '';
        for await (const chunk of req) {
          body += chunk;
          if (Buffer.byteLength(body) > 1024) return reply(413);
        }
        let supplied;
        try { supplied = JSON.parse(body).token; } catch { return reply(400); }
        if (!secretMatches(supplied, token)) return reply(403, 'Invalid access link.');
        res.setHeader('Set-Cookie', `${cookieName}=${token}; HttpOnly; SameSite=Strict; Path=/${secure ? '; Secure' : ''}`);
        return reply(204);
      }
      if (!['GET', 'HEAD'].includes(req.method)) { res.setHeader('Allow', 'GET, HEAD'); return reply(405); }
      if (path === '/') return reply(200, template.replaceAll('__NONCE__', nonce), 'text/html; charset=utf-8');
      const cookie = req.headers.cookie?.split(';').map(part => part.trim()).find(part => part.startsWith(`${cookieName}=`))?.slice(cookieName.length + 1);
      if (!secretMatches(cookie, token)) return reply(401, 'Open the access link from your chat.');
      if (path === '/api/evidence') {
        const entries = await getEntries(root);
        return reply(200, JSON.stringify(entries.map(({ storedName, ...entry }) => entry)), 'application/json; charset=utf-8');
      }
      const match = /^\/(media|download)\/([a-f0-9]{64})$/.exec(path);
      if (!match) return reply(404);
      const entry = await readJson(join(root, 'entries', `${match[2]}.json`));
      if (!entry || !/^[a-f0-9]{64}\.(png|jpe?g|webp|gif|mp4)$/.test(entry.storedName)) return reply(404);
      const media = await realpath(join(root, 'media', entry.storedName));
      if (!media.startsWith(mediaRoot + sep)) return reply(404);
      const handle = await open(media, 'r');
      try {
        const size = (await handle.stat()).size;
        const range = req.method === 'GET' && req.headers.range ? byteRange(req.headers.range, size) : undefined;
        if (range === null) { res.setHeader('Content-Range', `bytes */${size}`); return reply(416); }
        const name = entry.type === 'video' ? basename(entry.name, extname(entry.name)) + '.mp4' : entry.name;
        res.setHeader('Content-Type', entry.mime);
        res.setHeader('Content-Disposition', `${match[1] === 'download' ? 'attachment' : 'inline'}; filename="evidence${extname(entry.storedName)}"; filename*=UTF-8''${encodeURIComponent(name).replace(/['()*]/g, c => '%' + c.charCodeAt(0).toString(16))}`);
        res.setHeader('Accept-Ranges', 'bytes');
        res.setHeader('Content-Length', range ? range.end - range.start + 1 : size);
        if (range) res.setHeader('Content-Range', `bytes ${range.start}-${range.end}/${size}`);
        res.writeHead(range ? 206 : 200);
        if (req.method === 'HEAD') return res.end();
        await pipeline(handle.createReadStream({ ...range, autoClose: false }), res);
      } finally { await handle.close(); }
    } catch (error) {
      if (res.destroyed) return;
      if (res.headersSent) return res.destroy();
      reply(error.code === 'ENOENT' ? 404 : 500, 'Unable to read evidence.');
    }
  });
  server.requestTimeout = 15_000;
  server.headersTimeout = 10_000;
  return server;
}

async function sessionStatus(root) {
  const state = await readJson(join(root, 'session.json'));
  if (!state) return null;
  try {
    const response = await fetch(`http://127.0.0.1:${state.port}/control/status`, {
      headers: { 'X-Evidence-Control': state.controlToken }, signal: AbortSignal.timeout(2000),
    });
    return response.ok ? state : null;
  } catch { return null; }
}

async function serve(root, cloudflared) {
  await initialize(root);
  const lockPath = join(root, 'session.lock');
  try { await writeFile(lockPath, String(process.pid), { flag: 'wx' }); }
  catch (error) {
    if (error.code !== 'EEXIST') throw error;
    const owner = Number(await readFile(lockPath, 'utf8'));
    if (!Number.isSafeInteger(owner) || owner <= 0) throw new Error('Invalid session lock. Inspect session.lock.');
    try { process.kill(owner, 0); throw new Error('An evidence server is already running or starting.'); }
    catch (error) { if (error.code !== 'ESRCH') throw error; }
    await unlink(lockPath);
    await writeFile(lockPath, String(process.pid), { flag: 'wx' });
  }
  let tunnel, stopping = false, server;
  const token = randomBytes(32).toString('hex');
  const controlToken = randomBytes(32).toString('hex');
  const stop = async () => {
    if (stopping) return;
    stopping = true;
    server?.close();
    server?.closeAllConnections();
    // Only stop our own child, never a PID read from a saved file.
    if (tunnel && tunnel.exitCode === null) tunnel.kill();
    await unlink(lockPath);
  };
  try {
    server = await createEvidenceServer({ root, token, controlToken, onStop: () => void stop() });
    await new Promise((resolve, reject) => {
      server.once('error', reject);
      server.listen(0, '127.0.0.1', resolve);
    });
    const port = server.address().port;
    const state = { pid: process.pid, port, controlToken, url: null, startedAt: new Date().toISOString() };
    await atomicJson(join(root, 'session.json'), state);
    tunnel = spawn(cloudflared, ['tunnel', '--no-autoupdate', '--protocol', 'http2', '--url', `http://127.0.0.1:${port}`], {
      windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
    });
    let buffer = '', hostname, announced = false;
    const consume = chunk => {
      const line = chunk.toString();
      process.stderr.write(line);
      buffer = (buffer + line).slice(-16000);
      hostname ||= buffer.match(/https:\/\/[a-z0-9-]+\.trycloudflare\.com/)?.[0];
      if (!announced && hostname && buffer.includes('Registered tunnel connection')) {
        announced = true;
        state.url = `${hostname}/#token=${token}`;
        void atomicJson(join(root, 'session.json'), state).catch(error => { console.error(error.message); void stop(); });
      }
    };
    tunnel.stdout.on('data', consume);
    tunnel.stderr.on('data', consume);
    tunnel.on('error', error => { console.error(error.message); void stop(); });
    tunnel.on('exit', () => { void stop(); });
    process.on('SIGINT', () => { void stop(); });
    process.on('SIGTERM', () => { void stop(); });
  } catch (error) { await stop(); throw error; }
}

async function start(root, cloudflared) {
  const existing = await sessionStatus(root);
  if (existing?.url) return existing;
  if (!existing) {
    if (!cloudflared) throw new Error('Use tools/share_evidence.ps1 to install and start the tunnel.');
    await initialize(root);
    const log = await open(join(root, 'server.log'), 'a');
    try {
      const child = spawn(process.execPath, [script, 'serve', '--root', root, '--cloudflared', cloudflared], {
        detached: true, windowsHide: true, stdio: ['ignore', log.fd, log.fd],
      });
      await new Promise((resolve, reject) => { child.once('spawn', resolve); child.once('error', reject); });
      child.unref();
    } finally { await log.close(); }
  }
  for (let attempt = 0; attempt < 90; attempt++) {
    const state = await sessionStatus(root);
    if (state?.url) return state;
    await pause(500);
  }
  throw new Error(`Tunnel not ready. Inspect ${join(root, 'server.log')}; use Status to retry or Stop to close it.`);
}

async function main() {
  const command = process.argv[2];
  const { values, positionals } = parseArgs({ args: process.argv.slice(3), allowPositionals: true, options: {
    root: { type: 'string', default: defaultRoot }, cloudflared: { type: 'string' },
    title: { type: 'string' }, note: { type: 'string', default: '' },
  } });
  const root = resolve(values.root);
  if (command === 'add') {
    const entries = await addEvidence({ files: positionals, title: values.title, note: values.note, root });
    console.log(JSON.stringify({ added: entries.map(({ id, title, name, type, size }) => ({ id, title, name, type, size })) }, null, 2));
  } else if (command === 'start') {
    const { url, startedAt } = await start(root, values.cloudflared);
    console.log(JSON.stringify({ running: true, url, startedAt }, null, 2));
  } else if (command === 'serve') {
    await serve(root, values.cloudflared);
  } else if (command === 'status') {
    const state = await sessionStatus(root);
    console.log(JSON.stringify({ running: !!state, url: state?.url ?? null, count: state ? (await getEntries(root)).length : 0 }, null, 2));
  } else if (command === 'stop') {
    const state = await sessionStatus(root);
    if (state) await fetch(`http://127.0.0.1:${state.port}/control/stop`, { method: 'POST', headers: { 'X-Evidence-Control': state.controlToken }, signal: AbortSignal.timeout(2000) });
    console.log('Evidence sharing stopped.');
  } else {
    throw new Error('Usage: node tools/serve_mobile_pv.mjs add --title TITLE -- FILE... | start --cloudflared PATH | status | stop');
  }
}

if (process.argv[1] && resolve(process.argv[1]) === script) {
  main().catch(error => { console.error(error.message); process.exitCode = 1; });
}
