import { createReadStream, statSync } from "node:fs";
import { createServer } from "node:http";
import { basename, join } from "node:path";

const root = "D:/dev/algo_shoot/build/pv_gameplay";
const file = "refrain_gameplay_pv_mobile.mp4";
const path = join(root, file);
const size = statSync(path).size;
const port = Number(process.env.PORT || 8001);

createServer((req, res) => {
  const urlPath = decodeURIComponent((req.url || "/").split("?")[0]);
  if (urlPath !== "/" && urlPath !== `/${file}` && urlPath !== "/download") {
    res.writeHead(404);
    res.end("not found");
    return;
  }

  const common = {
    "Content-Type": "video/mp4",
    "Content-Disposition": `inline; filename="${basename(file)}"`,
    "Accept-Ranges": "bytes",
    "Cache-Control": "no-store",
    "Access-Control-Allow-Origin": "*",
  };

  if (urlPath === "/") {
    res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
    res.end(`<!doctype html><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Refrain Gameplay PV</title>
<body style="margin:0;background:#111;color:#eee;font-family:system-ui">
<video src="/${file}" controls playsinline preload="metadata" style="width:100%;height:auto"></video>
<p style="padding:16px;line-height:1.8">
  <a style="color:#9cf" href="/${file}">再生する</a><br>
  <a style="color:#9cf;font-weight:700" href="/download">ダウンロードする</a>
</p>
</body>`);
    return;
  }

  const disposition = urlPath === "/download" ? "attachment" : "inline";
  const range = req.headers.range;
  if (!range) {
    res.writeHead(200, {
      ...common,
      "Content-Disposition": `${disposition}; filename="${basename(file)}"`,
      "Content-Length": size,
    });
    if (req.method === "HEAD") return res.end();
    createReadStream(path).pipe(res);
    return;
  }

  const match = /^bytes=(\d*)-(\d*)$/.exec(range);
  if (!match) {
    res.writeHead(416, { "Content-Range": `bytes */${size}` });
    res.end();
    return;
  }

  const start = match[1] ? Number(match[1]) : 0;
  const end = match[2] ? Number(match[2]) : size - 1;
  if (start >= size || end >= size || start > end) {
    res.writeHead(416, { "Content-Range": `bytes */${size}` });
    res.end();
    return;
  }

  res.writeHead(206, {
    ...common,
    "Content-Disposition": `${disposition}; filename="${basename(file)}"`,
    "Content-Length": end - start + 1,
    "Content-Range": `bytes ${start}-${end}/${size}`,
  });
  if (req.method === "HEAD") return res.end();
  createReadStream(path, { start, end }).pipe(res);
}).listen(port, "0.0.0.0", () => {
  console.log(`serving http://0.0.0.0:${port}/${file}`);
});
