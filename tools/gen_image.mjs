// OpenAI 画像生成ユーティリティ（gpt-image-1）。
// 使い方: node gen_image.mjs <keyFile> <promptFile> <outPng> [size] [background] [quality] [model]
//   size: 1024x1024 | 1536x1024 | 1024x1536 (default 1024x1536)
//   background: transparent | opaque (default transparent)
//   quality: low | medium | high (default medium)
//   model: gpt-image-2 | gpt-image-1 | ... (default gpt-image-2)
// キーはファイルから読み込み、ログには出さない。
import fs from 'node:fs';

const [, , keyPath, promptPath, outPath,
  size = '1024x1536', background = 'transparent', quality = 'medium',
  model = 'gpt-image-2'] = process.argv;

if (!keyPath || !promptPath || !outPath) {
  console.error('usage: node gen_image.mjs <keyFile> <promptFile> <outPng> [size] [background] [quality]');
  process.exit(2);
}

const key = fs.readFileSync(keyPath, 'utf8').trim();
const prompt = fs.readFileSync(promptPath, 'utf8');

const body = { model, prompt, size, background, output_format: 'png', n: 1, quality };
console.log('model:', model, 'size:', size, 'bg:', background, 'quality:', quality);

const res = await fetch('https://api.openai.com/v1/images/generations', {
  method: 'POST',
  headers: { Authorization: `Bearer ${key}`, 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
});

const text = await res.text();
if (!res.ok) {
  console.error('HTTP', res.status);
  console.error(text.slice(0, 1200));
  process.exit(1);
}
let j;
try { j = JSON.parse(text); } catch { console.error('parse fail:', text.slice(0, 600)); process.exit(1); }

const b64 = j?.data?.[0]?.b64_json;
if (!b64) { console.error('no image in response:', JSON.stringify(j).slice(0, 800)); process.exit(1); }

fs.writeFileSync(outPath, Buffer.from(b64, 'base64'));
console.log('OK saved', outPath, fs.statSync(outPath).size, 'bytes');
if (j.usage) console.log('usage', JSON.stringify(j.usage));
