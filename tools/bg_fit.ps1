# 背景をアスペクト維持のセンタークロップで W x H に合わせ、任意で暗く/低コントラスト化して保存。
# 使い方: powershell -File tools/bg_fit.ps1 -In <in> -Out <out> -W 384 -H 216 [-Mul 1.0] [-Add 0]
param(
  [Parameter(Mandatory=$true)][string]$In,
  [Parameter(Mandatory=$true)][string]$Out,
  [int]$W = 384,
  [int]$H = 216,
  [double]$Mul = 1.0,
  [int]$Add = 0
)
$cs = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
public static class BgFit {
  public static string Run(string inPath,string outPath,int W,int H,double mul,int add){
    using(var src=new Bitmap(inPath)){
      int sw=src.Width, sh=src.Height;
      double ta=(double)W/H, sa=(double)sw/sh;
      int cw,ch;
      if(sa>ta){ ch=sh; cw=(int)Math.Round(sh*ta); } else { cw=sw; ch=(int)Math.Round(sw/ta); }
      int cx=(sw-cw)/2, cy=(sh-ch)/2;
      var o=new Bitmap(W,H,PixelFormat.Format32bppArgb);
      using(var g=Graphics.FromImage(o)){
        g.InterpolationMode=InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        g.CompositingQuality=CompositingQuality.HighQuality;
        g.DrawImage(src,new Rectangle(0,0,W,H),new Rectangle(cx,cy,cw,ch),GraphicsUnit.Pixel);
      }
      if(mul!=1.0||add!=0){
        var d=o.LockBits(new Rectangle(0,0,W,H),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        int n=d.Stride*H; byte[] b=new byte[n]; Marshal.Copy(d.Scan0,b,0,n);
        for(int i=0;i<W*H;i++){ int p=i*4;
          for(int c=0;c<3;c++){ int v=(int)Math.Round(b[p+c]*mul)+add; b[p+c]=(byte)(v<0?0:(v>255?255:v)); }
          b[p+3]=255; }
        Marshal.Copy(b,0,d.Scan0,n); o.UnlockBits(d);
      }
      o.Save(outPath,ImageFormat.Png); o.Dispose();
      return "ok "+sw+"x"+sh+" crop "+cw+"x"+ch+" -> "+W+"x"+H;
    }
  }
}
'@
Add-Type -TypeDefinition $cs -ReferencedAssemblies System.Drawing
[BgFit]::Run($In,$Out,$W,$H,$Mul,$Add)
