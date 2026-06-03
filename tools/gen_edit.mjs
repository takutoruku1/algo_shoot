// OpenAI 画像編集(参照画像ガイド付き生成)。元キャラを忠実に保ったまま描き直す用。
// 使い方: node gen_edit.mjs <keyFile> <refPng> <promptFile> <outPng> [size] [quality] [model]
import fs from 'node:fs';

const [, , keyPath, refPath, promptPath, outPath,
  size = '1024x1536', quality = 'high', model = 'gpt-image-2'] = process.argv;

if (!keyPath || !refPath || !promptPath || !outPath) {
  console.error('usage: node gen_edit.mjs <keyFile> <refPng> <promptFile> <outPng> [size] [quality] [model]');
  process.exit(2);
}

const key = fs.readFileSync(keyPath, 'utf8').trim();
const prompt = fs.readFileSync(promptPath, 'utf8');
const buf = fs.readFileSync(refPath);

const form = new FormData();
form.append('model', model);
form.append('prompt', prompt);
form.append('size', size);
form.append('quality', quality);
form.append('image', new Blob([buf], { type: 'image/png' }), 'ref.png');

console.log('edit model:', model, 'size:', size, 'quality:', quality, 'ref:', refPath);
const res = await fetch('https://api.openai.com/v1/images/edits', {
  method: 'POST',
  headers: { Authorization: `Bearer ${key}` },
  body: form,
});
const text = await res.text();
if (!res.ok) { console.error('HTTP', res.status); console.error(text.slice(0, 1400)); process.exit(1); }
let j; try { j = JSON.parse(text); } catch { console.error('parse fail:', text.slice(0, 600)); process.exit(1); }
const b64 = j?.data?.[0]?.b64_json;
if (!b64) { console.error('no image:', JSON.stringify(j).slice(0, 800)); process.exit(1); }
fs.writeFileSync(outPath, Buffer.from(b64, 'base64'));
console.log('OK saved', outPath, fs.statSync(outPath).size, 'bytes');
if (j.usage) console.log('usage', JSON.stringify(j.usage));
