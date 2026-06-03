# 単色背景(マゼンタ/緑)をキー抜き→1pxフリンジ除去→余白トリム→targetH へ縮小し透過PNG保存。
# 使い方: powershell -File key_trim_scale.ps1 -In <in> -Out <out> -Key magenta|green -TargetH 44
param(
  [Parameter(Mandatory=$true)][string]$In,
  [Parameter(Mandatory=$true)][string]$Out,
  [Parameter(Mandatory=$true)][ValidateSet('magenta','green','none')][string]$Key,
  [int]$TargetH = 44,
  [int]$Levels = 0,
  [int]$Outline = 0
)
$cs = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
public static class KeyTrimScale {
  public static string Run(string inPath, string outPath, string key, int targetH, int levels, int outline){
    using(var src0=new Bitmap(inPath)){
      int w=src0.Width,h=src0.Height;
      var bmp=new Bitmap(w,h,PixelFormat.Format32bppArgb);
      using(var g=Graphics.FromImage(bmp)){g.DrawImage(src0,0,0,w,h);}
      var rect=new Rectangle(0,0,w,h);
      var d=bmp.LockBits(rect,ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
      int n=d.Stride*h; byte[] buf=new byte[n]; Marshal.Copy(d.Scan0,buf,0,n);
      if(key!="none"){
        bool magenta = key=="magenta";
        Func<int,bool> isBg = (i)=>{ int p=i*4; int b=buf[p],gg=buf[p+1],r=buf[p+2];
          if(magenta) return r>=170 && b>=150 && (r-gg)>55 && (b-gg)>35 && gg<=135;
          else        return gg>=140 && r<=130 && b<=130 && (gg-r)>40 && (gg-b)>40; };
        Func<int,bool> tint = (i)=>{ int p=i*4; int b=buf[p],gg=buf[p+1],r=buf[p+2];
          if(magenta) return (r-gg)>35 && (b-gg)>20; else return (gg-r)>25 && (gg-b)>20; };
        // global key
        for(int i=0;i<w*h;i++) if(isBg(i)) buf[i*4+3]=0;
        // 1px defringe
        byte[] outA=new byte[w*h]; for(int i=0;i<w*h;i++) outA[i]=buf[i*4+3];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++){int i=y*w+x; if(buf[i*4+3]==0){outA[i]=0;continue;}
          if(tint(i)){bool nT=false;
            if(x>0&&buf[(i-1)*4+3]==0)nT=true; if(x<w-1&&buf[(i+1)*4+3]==0)nT=true;
            if(y>0&&buf[(i-w)*4+3]==0)nT=true; if(y<h-1&&buf[(i+w)*4+3]==0)nT=true; if(nT)outA[i]=0;}}
        for(int i=0;i<w*h;i++) buf[i*4+3]=outA[i];
      }
      Marshal.Copy(buf,0,d.Scan0,n); bmp.UnlockBits(d);
      // bbox
      int minX=w,minY=h,maxX=-1,maxY=-1;
      for(int y=0;y<h;y++)for(int x=0;x<w;x++){ if(buf[(y*w+x)*4+3]>16){ if(x<minX)minX=x; if(x>maxX)maxX=x; if(y<minY)minY=y; if(y>maxY)maxY=y; } }
      if(maxX<0) return "empty";
      int cw=maxX-minX+1, ch=maxY-minY+1;
      var crop=new Bitmap(cw,ch,PixelFormat.Format32bppArgb);
      using(var g=Graphics.FromImage(crop)){ g.DrawImage(bmp,new Rectangle(0,0,cw,ch),new Rectangle(minX,minY,cw,ch),GraphicsUnit.Pixel); }
      int th=targetH; int tw=(int)Math.Round(cw*(double)th/ch);
      var o=new Bitmap(tw,th,PixelFormat.Format32bppArgb);
      using(var g=Graphics.FromImage(o)){ g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.PixelOffsetMode=PixelOffsetMode.HighQuality; g.CompositingQuality=CompositingQuality.HighQuality; g.DrawImage(crop,new Rectangle(0,0,tw,th)); }
      // posterize + hard alpha (crisp pixel-art look)
      if(levels>0){
        var od=o.LockBits(new Rectangle(0,0,tw,th),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        int on=od.Stride*th; byte[] ob=new byte[on]; Marshal.Copy(od.Scan0,ob,0,on);
        int L=levels;
        for(int i=0;i<tw*th;i++){ int p=i*4;
          int a = ob[p+3] >= 110 ? 255 : 0; ob[p+3]=(byte)a;
          if(a==0) continue;
          for(int c=0;c<3;c++){ int v=ob[p+c]; int q=(int)Math.Round(v/255.0*(L-1))*255/(L-1); ob[p+c]=(byte)(q<0?0:(q>255?255:q)); }
        }
        Marshal.Copy(ob,0,od.Scan0,on); o.UnlockBits(od);
      }
      // add a dark outline (silhouette rim) so the character reads on any background
      if(outline>0){
        int pad=outline+1; int nw=tw+pad*2, nh=th+pad*2;
        var padded=new Bitmap(nw,nh,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(padded)){ g.Clear(Color.Transparent); g.DrawImage(o,pad,pad); }
        var pd=padded.LockBits(new Rectangle(0,0,nw,nh),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        int pn=pd.Stride*nh; byte[] pb=new byte[pn]; Marshal.Copy(pd.Scan0,pb,0,pn);
        bool[] cur=new bool[nw*nh]; bool[] solid=new bool[nw*nh]; bool[] outl=new bool[nw*nh];
        for(int i=0;i<nw*nh;i++){ bool s=pb[i*4+3]>=110; cur[i]=s; solid[i]=s; }
        for(int step=0;step<outline;step++){
          bool[] next=(bool[])cur.Clone();
          for(int y=0;y<nh;y++)for(int x=0;x<nw;x++){int i=y*nw+x; if(cur[i])continue; bool adj=false;
            for(int dy=-1;dy<=1&&!adj;dy++)for(int dx=-1;dx<=1;dx++){int xx=x+dx,yy=y+dy; if(xx<0||yy<0||xx>=nw||yy>=nh)continue; if(cur[yy*nw+xx]){adj=true;break;}}
            if(adj){ next[i]=true; if(!solid[i]) outl[i]=true; } }
          cur=next;
        }
        for(int i=0;i<nw*nh;i++){ if(outl[i] && pb[i*4+3]<110){ pb[i*4+0]=(byte)120; pb[i*4+1]=(byte)95; pb[i*4+2]=(byte)110; pb[i*4+3]=(byte)170; } }
        Marshal.Copy(pb,0,pd.Scan0,pn); padded.UnlockBits(pd);
        padded.Save(outPath,ImageFormat.Png); padded.Dispose(); crop.Dispose(); bmp.Dispose(); o.Dispose();
        return "ok outline "+nw+"x"+nh;
      }
      o.Save(outPath,ImageFormat.Png); crop.Dispose(); bmp.Dispose(); o.Dispose();
      return "ok "+cw+"x"+ch+" -> "+tw+"x"+th;
    }
  }
}
'@
Add-Type -TypeDefinition $cs -ReferencedAssemblies System.Drawing
[KeyTrimScale]::Run($In,$Out,$Key,$TargetH,$Levels,$Outline)
