// OpenAI Responses API で GPT-6 Astra（テキスト）に相談するユーティリティ。
// 使い方: node astra_ask.mjs <keyFile> <promptFile> <outMd> [model]
//   model: gpt-6-astra（既定）| gpt-5 など
// キーはファイルから読み込み、ログには出さない。応答本文を outMd に保存する。
import fs from 'node:fs';
const [, , keyPath, promptPath, outPath, model = 'gpt-6-astra'] = process.argv;
if (!keyPath || !promptPath || !outPath) {
  console.error('usage: node astra_ask.mjs <keyFile> <promptFile> <outMd> [model]');
  process.exit(2);
}
const key = fs.readFileSync(keyPath, 'utf8').trim();
const prompt = fs.readFileSync(promptPath, 'utf8');
const body = { model, input: prompt };
console.log('model:', model, 'prompt chars:', prompt.length);
const t0 = Date.now();
const res = await fetch('https://api.openai.com/v1/responses', {
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
const json = JSON.parse(text);
let out = json.output_text;
if (!out) {
  out = (json.output || [])
    .flatMap((o) => (o.content || []))
    .filter((c) => c.type === 'output_text')
    .map((c) => c.text)
    .join('\n');
}
fs.writeFileSync(outPath, out ?? text, 'utf8');
const u = json.usage || {};
console.log('done', ((Date.now() - t0) / 1000).toFixed(1) + 's',
  'in:', u.input_tokens, 'out:', u.output_tokens, '->', outPath);
