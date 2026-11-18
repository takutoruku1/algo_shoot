import fs from "node:fs";
const folder = "char/v3/enemies/cutout/";
const humans = JSON.parse(fs.readFileSync(folder+"human_layers.json", "utf8").replace(/^\uFEFF/, ""));
const num = n => Number(n.toFixed(5));
const vec = (x,y) => "Vector2(" + num(x) + ", " + num(y) + ")";
const rad = n => {
 const value = num(n * Math.PI / 180);
 return Number.isInteger(value) ? value + ".0" : String(value);
};
const propRects = {
 akari_desk:[[28,92,454,367],[542,49,325,498],[886,145,633,319],[58,596,267,371],[511,596,251,360],[850,647,649,297]],
 akari_undo:[[50,157,456,310],[544,198,470,246],[1042,195,452,263],[47,653,498,210],[590,600,664,351]],
 rei_icon:[[213,54,439,465],[940,115,373,390],[231,598,385,387],[1082,729,99,101]],
 rei_bubble:[[42,233,901,746],[960,74,457,306],[1100,453,390,236],[1213,824,64,64]],
 koharu_penlight:[[112,26,370,580],[655,231,247,354],[1090,202,384,190],[64,717,397,220],[591,745,333,181],[1043,643,463,286]],
 koharu_box:[[46,155,423,365],[575,183,368,353],[1070,187,409,369],[38,719,470,120],[532,717,476,120],[1035,690,478,242]],
};
const propLayout = {
 akari_desk:[[35,-210,340,340,2],[-180,-100,270,420,-2],[0,20,650,260,1],[-215,205,180,280,0],[210,205,180,280,0],[0,285,680,160,-3]],
 akari_undo:[[0,-230,550,180,3],[15,-60,550,170,2],[-10,100,550,170,1],[10,245,550,170,0],[0,305,620,150,-2]],
 rei_icon:[[0,65,400,425,2],[-185,-140,340,365,0],[185,-165,340,365,0],[100,-155,65,65,3]],
 rei_bubble:[[-35,105,590,420,2],[100,-195,360,265,0],[205,-70,280,215,1],[190,-310,45,45,3]],
 koharu_penlight:[[35,-105,300,480,1],[-120,235,180,260,1],[75,-230,320,80,2],[0,-40,330,95,2],[-65,155,310,95,2],[-65,320,620,140,-1]],
 koharu_box:[[0,-210,400,310,3],[0,0,400,310,2],[0,200,400,310,1],[0,-35,580,90,4],[0,175,580,90,4],[0,325,650,150,-1]],
};
function animation(id, length, loop, tracks) {
 let text = '[sub_resource type="Animation" id="' + id + '"]\nlength = ' + length + '\nloop_mode = ' + (loop?1:0) + '\n';
 tracks.forEach((t,i) => {
  const times = t.times || t.values.map((_,k)=>length*k/(t.values.length-1));
  text += 'tracks/'+i+'/type = "value"\ntracks/'+i+'/path = NodePath("'+t.path+'")\ntracks/'+i+'/interp = 1\ntracks/'+i+'/loop_wrap = true\ntracks/'+i+'/keys = {\n"times": PackedFloat32Array('+times.map(num).join(", ")+'),\n"transitions": PackedFloat32Array('+times.map(()=>1).join(", ")+'),\n"update": '+(t.capture?2:t.discrete?1:0)+',\n"values": ['+t.values.join(", ")+']\n}\n';
 });
 return text+'\n';
}

function inside(point, polygon) {
 let result=false;
 for(let i=0,j=polygon.length-1;i<polygon.length;j=i++){
  const [x,y]=point,[xi,yi]=polygon[i],[xj,yj]=polygon[j];
  if((yi>y)!==(yj>y) && x<(xj-xi)*(y-yi)/(yj-yi)+xi) result=!result;
 }
 return result;
}
function edgeDistance(point, polygon) {
 let nearest=Infinity;
 for(let i=0;i<polygon.length;i++){
  const a=polygon[i],b=polygon[(i+1)%polygon.length],dx=b[0]-a[0],dy=b[1]-a[1];
  const t=Math.max(0,Math.min(1,((point[0]-a[0])*dx+(point[1]-a[1])*dy)/(dx*dx+dy*dy)));
  nearest=Math.min(nearest,Math.hypot(point[0]-a[0]-t*dx,point[1]-a[1]-t*dy));
 }
 return nearest;
}
const smooth = n => {const t=Math.max(0,Math.min(1,n));return t*t*(3-2*t);};
function originalScene(id,p) {
 const art="char/v3/enemies/"+p.stage+"/enemy_"+id+"_pre.png";
 const png=fs.readFileSync(art),w=png.readUInt32BE(16),h=png.readUInt32BE(20),step=16;
 const cols=Math.ceil(w/step),rows=Math.ceil(h/step),uv=[],weights=[];
 for(let y=0;y<=rows;y++) for(let x=0;x<=cols;x++){
  const pt=[Math.min(x*step,w),Math.min(y*step,h)];
  const foreground=p.front.some(poly=>inside(pt,poly)||edgeDistance(pt,poly)<12);
  const rigid=p.rigid.some(poly=>inside(pt,poly)||edgeDistance(pt,poly)<24);
  let weight=0;
  if(!foreground){
   if(rigid) weight=1;
   else if(inside(pt,p.moving))
    weight=smooth(edgeDistance(pt,p.moving)/28)*smooth((Math.hypot(pt[0]-p.pivot[0],pt[1]-p.pivot[1])-8)/48);
  }
  uv.push(pt); weights.push(num(weight));
 }
 const layers=[[],[],[]];
 for(let y=0;y<rows;y++) for(let x=0;x<cols;x++){
  const a=y*(cols+1)+x,b=a+1,c=a+cols+1,d=c+1;
  for(const tri of [[a,b,c],[b,d,c]]){
   const center=tri.reduce((sum,i)=>[sum[0]+uv[i][0]/3,sum[1]+uv[i][1]/3],[0,0]);
   const layer=tri.some(i=>weights[i]>0)?1:p.front.some(poly=>inside(center,poly))?2:0;
   layers[layer].push(tri);
  }
 }
 const rot=(path,values)=>({path:path+":rotation",values:values.map(rad)});
 let text='[gd_scene format=3]\n\n[ext_resource type="Script" path="res://src/fx/EnemyCutout.cs" id="1"]\n[ext_resource type="Texture2D" path="res://'+art+'" id="2"]\n\n';
 const gesture="Torso/Action/Gesture",pivot=[p.pivot[0]-w/2,p.pivot[1]-h/2];
 const idleTimes=[0,.1,.2,.34,.46,.58,.7,.85,1].map(t=>t*p.length);
 const idleValues=[0,0,.35,1,1,.45,-.18,0,0];
 const move=(path,offset,values,origin=[0,0])=>({path:path+":position",values:values.map(v=>vec(origin[0]+offset[0]*v,origin[1]+offset[1]*v))});
 text+=animation("Idle",p.length,true,[
  {...rot(gesture,idleValues.map(v=>v*p.idle)),times:idleTimes},
  {...move(gesture,p.lift,idleValues,pivot),times:idleTimes}
 ]);
 text+=animation("Anticipate",.6,false,[
  {...rot(gesture+"/Action",[0,p.charge,p.charge,0]),times:[0,.2,.42,.6],capture:true},
  {...move(gesture+"/Action",p.lift,[0,-.6,-.6,0]),times:[0,.2,.42,.6],capture:true}
 ]);
 text+=animation("Recoil",.56,false,[
  {...rot(gesture+"/Action",[0,p.hit,p.hit*.7,-p.hit*.15,0]),times:[0,.14,.22,.38,.56],capture:true},
  {...move(gesture+"/Action",p.lift,[0,1.3,1,-.15,0]),times:[0,.14,.22,.38,.56],capture:true}
 ]);
 text+='[sub_resource type="AnimationLibrary" id="IdleLibrary"]\n_data = { &"idle": SubResource("Idle") }\n\n[sub_resource type="AnimationLibrary" id="ActionLibrary"]\n_data = { &"anticipate": SubResource("Anticipate"), &"recoil": SubResource("Recoil") }\n\n';
 text+='[node name="Cutout" type="Node2D"]\nscript = ExtResource("1")\n\n[node name="Torso" type="Node2D" parent="."]\n\n[node name="Action" type="Skeleton2D" parent="Torso"]\n\n';
 for(const [name,parent,x,y] of [["Pose","Torso/Action",0,0],["Gesture","Torso/Action",p.pivot[0]-w/2,p.pivot[1]-h/2],["Action","Torso/Action/Gesture",0,0]])
  text+='[node name="'+name+'" type="Bone2D" parent="'+parent+'"]\nposition = '+vec(x,y)+'\nrest = Transform2D(1, 0, 0, 1, '+num(x)+', '+num(y)+')\nauto_calculate_length_and_angle = false\nlength = 1.0\nbone_angle = 0.0\n\n';
 layers.forEach((triangles,layer)=>{
  const indices=[...new Set(triangles.flat())],local=new Map(indices.map((value,i)=>[value,i]));
  text+='[node name="'+["BackLayer","GestureLayer","FrontLayer"][layer]+'" type="Polygon2D" parent="."]\nz_index = '+layer+'\ntexture_filter = 2\ntexture = ExtResource("2")\nskeleton = NodePath("../Torso/Action")\n';
  text+='polygon = PackedVector2Array('+indices.flatMap(i=>[uv[i][0]-w/2,uv[i][1]-h/2]).join(", ")+')\n';
  text+='uv = PackedVector2Array('+indices.flatMap(i=>uv[i]).join(", ")+')\n';
  text+='polygons = ['+triangles.map(tri=>'PackedInt32Array('+tri.map(i=>local.get(i)).join(", ")+')').join(", ")+']\n';
  text+='bones = [NodePath("Pose"), PackedFloat32Array('+indices.map(i=>num(1-weights[i])).join(", ")+'), NodePath("Gesture/Action"), PackedFloat32Array('+indices.map(i=>weights[i]).join(", ")+')]\n\n';
 });
 for(const name of ["Idle","Action"])
  text+='[node name="'+name+'Animation" type="AnimationPlayer" parent="."]\ncallback_mode_process = 2\nlibraries = {\n&"": SubResource("'+name+'Library")\n}\n\n';
 fs.writeFileSync(folder+id+".tscn",text);
}
function propScene(id, rects) {
 const atlas=id;
 let res='[gd_scene format=3]\n\n[ext_resource type="Script" path="res://src/fx/EnemyCutout.cs" id="1"]\n[ext_resource type="Texture2D" path="res://'+folder+atlas+'_atlas_v1.png" id="2"]\n[ext_resource type="Shader" path="res://shaders/enemy_cutout_alpha.gdshader" id="3"]\n\n[sub_resource type="ShaderMaterial" id="Alpha"]\nshader = ExtResource("3")\n\n';
 rects.forEach((r,i)=>{ res+='[sub_resource type="AtlasTexture" id="Part'+i+'"]\natlas = ExtResource("2")\nregion = Rect2('+r.slice(0,4).join(", ")+')\nfilter_clip = true\n\n'; });
 let nodes='[node name="Cutout" type="Node2D"]\nscript = ExtResource("1")\n\n[node name="Torso" type="Node2D" parent="."]\n\n[node name="Action" type="Node2D" parent="Torso"]\n\n';
 const root="Torso/Action", idle=[], action=[], blink=[];
 const joint=(name,x,y,z=0,parent=root)=>{
   nodes+='[node name="'+name+'" type="Node2D" parent="'+parent+'"]\nposition = '+vec(x,y)+'\nz_index = '+z+'\n\n[node name="Action" type="Node2D" parent="'+parent+'/'+name+'"]\n\n';
   return parent+"/"+name+"/Action";
 };
 const sprite=(name,parent,index,w,h,pivot=[.5,.5],flip=false,visible=true)=>{
   nodes+='[node name="'+name+'" type="Sprite2D" parent="'+parent+'"]\nvisible = '+visible+'\nmaterial = SubResource("Alpha")\nposition = '+vec(-w*pivot[0],-h*pivot[1])+'\nscale = '+vec(w/rects[index][2],h/rects[index][3])+'\ntexture = SubResource("Part'+index+'")\ncentered = false\nflip_h = '+flip+'\ntexture_filter = 2\n\n';
 };
 const rot=(path,values)=>({path:path+":rotation",values:values.map(rad)});

   propLayout[id].forEach(([x,y,w,h,z],i)=>{
     const name="Part"+i,path=joint(name,x,y,z);
     sprite("Art",path,i,w,h);
     let a=(i%2?1:-1)*(id==="akari_desk"?3:id==="koharu_box"?7:10);
     idle.push(rot(root+"/"+name,[0,a,0,-a,0]));
     if(id!=="akari_desk" || i===1)
       idle.push({path:root+"/"+name+":position",values:[vec(x,y),vec(x+(i%2?8:-8),y-12*(i%3+1)),vec(x,y),vec(x-(i%2?8:-8),y+6),vec(x,y)]});
     action.push({name,charge:a*1.4,hit:-a*1.7});
   });
 const charge=[],hit=[];
 for(const a of action) {
   const path=root+"/"+a.name+"/Action";
   charge.push({...rot(path,[0,a.charge,a.charge,0]),times:[0,.12,.42,.6]});
   hit.push({...rot(path,[0,a.hit,-a.hit*.22,0]),times:[0,.10,.28,.48]});
 }
 res+=animation("Idle",3.1,true,idle);
 res+=animation("Anticipate",.6,false,charge);
 res+=animation("Recoil",.48,false,hit);
 res+=animation("Blink",4.7,true,blink);
 res+='[sub_resource type="AnimationLibrary" id="IdleLibrary"]\n_data = { &"idle": SubResource("Idle") }\n\n';
 res+='[sub_resource type="AnimationLibrary" id="ActionLibrary"]\n_data = { &"anticipate": SubResource("Anticipate"), &"recoil": SubResource("Recoil") }\n\n';
 res+='[sub_resource type="AnimationLibrary" id="BlinkLibrary"]\n_data = { &"blink": SubResource("Blink") }\n\n';
 for(const name of ["Idle","Action","Blink"])
   nodes+='[node name="'+name+'Animation" type="AnimationPlayer" parent="."]\ncallback_mode_process = 2\nlibraries = {\n&"": SubResource("'+name+'Library")\n}\n\n';
 fs.writeFileSync(folder+id+".tscn",res+nodes);
}
for(const [id,p] of Object.entries(humans)) originalScene(id,p);
for(const [id,rects] of Object.entries(propRects)) propScene(id,rects);
console.log("Built 12 original-pixel human rigs and 6 prop cutouts with native AnimationPlayer tracks.");
