param([string]$AtlasDirectory = 'char/v3/enemies/cutout')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$drawingReferences = @([System.Drawing.Bitmap].Assembly.Location, [object].Assembly.Location) + @([System.Drawing.Bitmap].Assembly.GetReferencedAssemblies() | ForEach-Object { [Reflection.Assembly]::Load($_).Location })
Add-Type -ReferencedAssemblies $drawingReferences -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class EnemyCutoutBounds {
    public class Result { public int width; public int height; public int[][] parts; }
    public static object Read(string path) {
        using var image = new Bitmap(path);
        int w = image.Width, h = image.Height;
        var data = image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] pixels = new byte[data.Stride * h];
        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        image.UnlockBits(data);
        var seen = new bool[w * h];
        var queue = new int[w * h];
        var parts = new int[2048][];
        int count = 0;
        for (int start = 0; start < seen.Length; start++) {
            if (seen[start] || pixels[start * 4 + 3] < 80) continue;
            int head = 0, tail = 1, left = w, top = h, right = 0, bottom = 0;
            queue[0] = start; seen[start] = true;
            while (head < tail) {
                int at = queue[head++], x = at % w, y = at / w;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                if (x > 0) Visit(at - 1);
                if (x < w - 1) Visit(at + 1);
                if (y > 0) Visit(at - w);
                if (y < h - 1) Visit(at + w);
                void Visit(int p) {
                    if (seen[p] || pixels[p * 4 + 3] < 80) return;
                    seen[p] = true; queue[tail++] = p;
                }
            }
            if (tail > 1500) parts[count++] = new[] {left, top, right - left + 1, bottom - top + 1, tail};
        }
        Array.Resize(ref parts, count);
        return new Result { width = w, height = h, parts = parts };
    }
}
'@
Get-ChildItem -LiteralPath $AtlasDirectory -Filter '*_atlas_v1.png' | ForEach-Object {
    [pscustomobject]@{ id = $_.BaseName.Replace('_atlas_v1', ''); atlas = [EnemyCutoutBounds]::Read($_.FullName) }
} | ConvertTo-Json -Depth 6
