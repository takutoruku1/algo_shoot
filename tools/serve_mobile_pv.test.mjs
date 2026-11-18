import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { mkdtemp, readFile, mkdir, writeFile, rm } from 'node:fs/promises';
import { join, resolve, sep } from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { request as httpRequest } from 'node:http';
import { addEvidence, byteRange, createEvidenceServer, defaultRoot } from './serve_mobile_pv.mjs';

const execute = promisify(execFile);
let root, server, origin, entry, cookie;
const token = 'a'.repeat(64), controlToken = 'b'.repeat(64);
const picture = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=', 'base64');
const request = (path, options = {}) => fetch(origin + path, { ...options, headers: { Cookie: cookie, ...options.headers } });

before(async () => {
  await mkdir(defaultRoot, { recursive: true });
  await writeFile(join(defaultRoot, '.gdignore'), '');
  root = await mkdtemp(join(defaultRoot, 'test-'));
  const source = join(root, '確認画像.png');
  await writeFile(source, picture);
  [entry] = await addEvidence({ files: [source], title: '<script>unsafe</script>', note: 'test', root });
  server = await createEvidenceServer({ root, token, controlToken });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  origin = `http://127.0.0.1:${server.address().port}`;
  const session = await fetch(origin + '/session', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ token }) });
  assert.equal(session.status, 204);
  cookie = session.headers.get('set-cookie').split(';')[0];
  assert.match(session.headers.get('set-cookie'), /HttpOnly; SameSite=Strict; Path=\/; Secure/);
});

after(async () => {
  if (server) { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
  if (root && resolve(root).startsWith(resolve(defaultRoot) + sep) && root.includes('test-')) await rm(root, { recursive: true });
});

test('anonymous visitors cannot list, read, download, or stop', async () => {
  for (const path of ['/api/evidence', `/media/${entry.id}`, `/download/${entry.id}`]) {
    assert.equal((await fetch(origin + path)).status, 401);
  }
  assert.equal((await request('/control/status')).status, 404);
  assert.equal((await request('/control/stop', { method: 'POST' })).status, 404);
  const bad = await fetch(origin + '/session', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ token: 'wrong' }) });
  assert.equal(bad.status, 403);
  const form = await fetch(origin + '/session', { method: 'POST', body: JSON.stringify({ token }) });
  assert.equal(form.status, 415);
  const large = await fetch(origin + '/session', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: 'a'.repeat(1025) });
  assert.equal(large.status, 413);
});

test('gallery is isolated and contains no token or local paths', async () => {
  const response = await fetch(origin + '/');
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('referrer-policy'), 'no-referrer');
  assert.match(response.headers.get('content-security-policy'), /frame-ancestors 'none'/);
  const html = await response.text();
  assert.ok(!html.includes(token));
  assert.ok(!html.includes(root));
  assert.ok(!html.includes('__NONCE__'));
  const entries = await (await request('/api/evidence')).json();
  assert.equal(entries.length, 1);
  assert.equal(entries[0].title, '<script>unsafe</script>');
  assert.ok(!JSON.stringify(entries).includes(root));
  assert.ok(!('storedName' in entries[0]));
});

test('registered image is unchanged and downloads have UTF-8 names', async () => {
  const response = await request(`/media/${entry.id}`);
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('content-type'), 'image/png');
  assert.deepEqual(Buffer.from(await response.arrayBuffer()), picture);
  const download = await request(`/download/${entry.id}`);
  assert.match(download.headers.get('content-disposition'), /attachment;.*filename\*=UTF-8''%/);
  await download.arrayBuffer();
  const head = await request(`/media/${entry.id}`, { method: 'HEAD', headers: { Range: 'bytes=0-1' } });
  assert.equal(head.status, 200);
  assert.equal(Number(head.headers.get('content-length')), picture.length);
  assert.equal((await head.arrayBuffer()).byteLength, 0);
});

test('video-style range requests support seeking and suffixes', async () => {
  for (const [range, start, end] of [['bytes=0-1', 0, 1], ['bytes=5-', 5, picture.length - 1], ['bytes=-8', picture.length - 8, picture.length - 1], ['bytes=3-999999', 3, picture.length - 1]]) {
    const response = await request(`/media/${entry.id}`, { headers: { Range: range } });
    assert.equal(response.status, 206);
    assert.equal(response.headers.get('content-range'), `bytes ${start}-${end}/${picture.length}`);
    assert.deepEqual(Buffer.from(await response.arrayBuffer()), picture.subarray(start, end + 1));
  }
  for (const range of ['bytes=999999-', 'bytes=8-3', 'bytes=-0', 'bytes=-', 'bytes=0-1,3-4', 'bytes=9999999999999999999999-']) {
    const response = await request(`/media/${entry.id}`, { headers: { Range: range } });
    assert.equal(response.status, 416, range);
    assert.equal(response.headers.get('content-range'), `bytes */${picture.length}`);
  }
  assert.equal(byteRange('bytes=0-0', 0), null);
});

test('files outside the catalog and traversal paths are never served', async () => {
  for (const path of ['/session.json', '/server.log', '/.openai_key.txt', '/media/../../.openai_key.txt', '/media/%2e%2e%2f.env', '/media/' + 'f'.repeat(64)]) {
    assert.equal((await request(path)).status, 404, path);
  }
  assert.equal((await request(`/media/${entry.id}`, { method: 'DELETE' })).status, 405);
  await assert.rejects(addEvidence({ files: [join(root, '.gdignore')], root }), /Unsupported media type/);
  const forged = { ...entry, storedName: '../確認画像.png' };
  const record = join(root, 'entries', `${entry.id}.json`);
  const original = await readFile(record);
  await writeFile(record, JSON.stringify(forged));
  try { assert.equal((await request(`/media/${entry.id}`)).status, 404); }
  finally { await writeFile(record, original); }
});

test('only local control credentials can manage the server', async () => {
  assert.equal((await request('/control/status', { headers: { 'X-Evidence-Control': controlToken } })).status, 200);
  const status = await new Promise((resolve, reject) => {
    const req = httpRequest(origin + '/control/status', { headers: { 'X-Evidence-Control': controlToken, Host: 'public.trycloudflare.com' } }, res => { res.resume(); resolve(res.statusCode); });
    req.on('error', reject); req.end();
  });
  assert.equal(status, 404);
});

test('AVI becomes mobile MP4 with H.264, AAC and early metadata', async () => {
  const avi = join(root, 'sample.avi');
  await execute('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-f', 'lavfi', '-i', 'testsrc2=size=320x240:rate=12', '-f', 'lavfi', '-i', 'sine=frequency=440', '-t', '0.5', '-c:v', 'mjpeg', '-c:a', 'pcm_s16le', avi], { windowsHide: true });
  const [video] = await addEvidence({ files: [avi], title: 'Video conversion test', root });
  const mp4 = join(root, 'media', video.storedName);
  const { stdout } = await execute('ffprobe', ['-v', 'error', '-show_streams', '-of', 'json', mp4], { windowsHide: true });
  const streams = JSON.parse(stdout).streams;
  assert.ok(streams.some(stream => stream.codec_name === 'h264' && stream.pix_fmt === 'yuv420p'));
  assert.ok(streams.some(stream => stream.codec_name === 'aac'));
  const content = await readFile(mp4);
  assert.ok(content.indexOf('moov') < content.indexOf('mdat'));
  const response = await request(`/media/${video.id}`, { headers: { Range: 'bytes=-64' } });
  assert.equal(response.status, 206);
  assert.equal(response.headers.get('content-type'), 'video/mp4');
  assert.equal((await response.arrayBuffer()).byteLength, 64);
});
