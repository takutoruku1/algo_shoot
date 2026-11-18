import math

ROOT = "D:/dev/algo_shoot/"
PROJECT = "Refrain - Boss Transformations 20261118"
SOURCES = [
    ("akari", "Unsent Letters", "char/v3/boss_akari_body_idle_v3.png", "char/v3/animal_forms/akari_animal_idle_v1.png", "char/v3/fx/akari/card_unsent_1.png", (0.96, 0.66, 0.28), 720, 720, 296),
    ("koharu", "Lights That Cannot Rest", "char/v3/boss_koharu_body_idle.png", "char/v3/animal_forms/koharu_animal_idle_v1.png", "char/v3/fx/koharu/penlight_lit.png", (0.48, 0.89, 0.76), 720, 720, 80),
    ("rei", "Beyond the Frame", "char/v3/boss_rei_body_idle.png", "char/v3/animal_forms/rei_animal_idle_v1.png", "char/v3/fx/rei/frame_star.png", (0.72, 0.53, 0.96), 720, 720, 255),
    ("mina", "Shelter of Wings", "assets/source/boss_cutins/mina_rain_idle.png", "char/v3/mina_dragon/mina_dragon_idle_v1.png", "char/ui/blender/unfolder_mina.png", (0.46, 0.75, 0.94), 837, 720, 256),
]


def ease(start, length):
    return f"(0.5-0.5*cos(3.141592654*min(max((time-{start})/{length},0),1)))"


def build(resolve, indices=(0, 1, 2, 3)):
    pm = resolve.GetProjectManager()
    project = pm.GetCurrentProject()
    assert project.GetName() == PROJECT
    pool = project.GetMediaPool()
    carrier = pool.ImportMedia([ROOT + "assets/source/boss_cutins/carrier.mp4"])[0]
    report = []
    for index in indices:
        ident, title, before, after, motif, color, bh, ah, mw = SOURCES[index]
        name = f"{index + 1:02d} - {title} - Transformation R3"
        timeline = pool.CreateEmptyTimeline(name, "00:00:00:00")
        assert timeline, name
        item = pool.AppendToTimeline([{"mediaPoolItem": carrier, "startFrame": 0, "endFrame": 180, "mediaType": 1, "recordFrame": 0}])[0]
        comp = item.AddFusionComp()
        comp.Lock()
        try:
            comp.SetPrefs({"Comp.FrameFormat.Width": 1280, "Comp.FrameFormat.Height": 720, "Comp.FrameFormat.Rate": 60})
            def node(kind, label):
                n = comp.AddTool(kind, xpos=-32768, ypos=-32768, autoconnect=False, automerge=False)
                n.SetAttrs({"TOOLS_Name": label})
                return n

            def expr(n, key, value):
                n[key].SetExpression(value)

            def bg(label, rgb):
                n = node("Background", label)
                n.Width = 1280
                n.Height = 720
                n.TopLeftRed, n.TopLeftGreen, n.TopLeftBlue = rgb
                n.TopLeftAlpha = 1
                return n

            def media(path, label):
                clip = pool.ImportMedia([ROOT + path])[0]
                n = node("MediaIn", label)
                n.MediaSource = "MediaPool"
                n.MediaID = clip.GetMediaId()
                return n

            def merge(back, fore, label, size=1, center="Point(0.5,0.5)", blend="1"):
                n = node("Merge", label)
                n.Background = back.Output
                n.Foreground = fore.Output
                n.Size = size
                expr(n, "Center", center)
                expr(n, "Blend", blend)
                return n

            def glow(source, label, radius=22, gain=1.3):
                n = node("SoftGlow", label)
                n.Input = source.Output
                n.Threshold = .1
                n.Gain = gain
                n.XGlowSize = radius
                n.RedScale, n.GreenScale, n.BlueScale = color
                return n

            def silhouette(source, label):
                n = node("ChannelBoolean", label)
                n.Background = source.Output
                n.ToRed, n.ToGreen, n.ToBlue, n.ToAlpha = 3, 3, 3, 3
                return glow(n, label + "_Bloom", 28, 1.5)

            def transparent(label):
                n = bg(label, (0, 0, 0))
                n.TopLeftAlpha = 0
                return n

            base = bg("Midnight", (0.006, 0.009, 0.018))
            scene = media(f"char/bg2/boss/{ident}_v1.png", "Existing_Stage")
            gather, release = ease(6, 62), ease(77, 36)
            rush = node("DirectionalBlur", "Stage_Impact_Zoom")
            rush.Input = scene.Output
            rush.Type = 3
            expr(rush, "Length", f"0.065*{ease(74,4)}*(1-{ease(79,22)})")
            layer = merge(base, rush, "Stage_Atmosphere", 0.87,
                          blend=f"0.28-0.24*{gather}+0.20*{ease(103,56)}")
            light = bg("Character_Color", color)
            white = bg("Light_Core", tuple(.82 + .18*c for c in color))
            accent_color = {"akari": (1,.28,.055), "koharu": (.7,.3,1), "rei": (1,.71,.22), "mina": (.28,.3,1)}[ident]
            accent = bg("Secondary_Light", accent_color)
            energy = transparent("Energy_Plate")
            noise = node("FastNoise", "Turbulent_Energy")
            noise.Detail, noise.Contrast, noise.Brightness = 4, 2.8, -.2
            noise.XScale, noise.YScale = 5, 5
            noise.Color1Red, noise.Color1Green, noise.Color1Blue, noise.Color1Alpha = 0, 0, 0, 0
            noise.Color2Red, noise.Color2Green, noise.Color2Blue, noise.Color2Alpha = *color, 1
            expr(noise, "Seethe", "time*0.018")
            corona = node("EllipseMask", "Plasma_Corona")
            corona.Solid, corona.BorderWidth, corona.SoftEdge = 0, .075, .025
            expr(corona, "Width", f".67-.5*{gather}+1.4*{release}")
            expr(corona, "Height", f"1.2-.9*{gather}+2.5*{release}")
            plasma = merge(energy, noise, "Plasma_Rim", blend=f"{ease(8,25)}*(1-{ease(100,45)})")
            plasma.EffectMask = corona.Mask
            energy = plasma
            for k in range(6):
                halo = node("EllipseMask", f"Gather_Halo_{k}")
                halo.Solid = 0
                halo.BorderWidth = .006 if k % 3 == 0 else .0025
                halo.SoftEdge = .003
                expr(halo, "Width", f"{.48+k*.13}*(1-0.83*{gather})+1.6*{release}")
                expr(halo, "Height", f"{.86+k*.20}*(1-0.83*{gather})+2.6*{release}")
                expr(halo, "Angle", f"{(k-2)*23}+time*{.7 if k%2 else -.5}")
                if ident == "koharu":
                    expr(halo, "Height", f"1.6*(1-0.78*{gather})+1.8*{release}")
                elif ident == "mina":
                    expr(halo, "Width", f"{.26+k*.08}*(1-0.78*{gather})+1.5*{release}")
                ring = merge(energy, light if k%2 else accent, f"Converging_Ring_{k}",
                             blend=f"{ease(k*3,16)}*(0.55+0.45*{gather})*(1-{ease(84,38)})")
                ring.EffectMask = halo.Mask
                energy = ring
            if ident in ("koharu", "mina"):
                for k in range(10 if ident == "koharu" else 5):
                    beam = node("RectangleMask", f"Ascension_Column_{k}")
                    beam.Height, beam.SoftEdge = 2, .028
                    expr(beam, "Width", f"{.02+k%3*.014}*(.5+{gather})")
                    if ident == "koharu":
                        beam.Center = [.12+k*.084, .5]
                        expr(beam, "Angle", f"{(k-4.5)*9}*(1-.55*{gather})")
                    else:
                        expr(beam, "Center", f"Point(.5+{(k-2)*.09}*(1-{gather}+2*{release}),.5)")
                    beam_layer = merge(energy, white if k%3 == 0 else accent, f"Rising_Light_{k}",
                                       blend=f".7*{ease(25+k*2,22)}*(1-{ease(83,23)})")
                    beam_layer.EffectMask = beam.Mask
                    energy = beam_layer
            layer = merge(layer, glow(energy, "Energy_Bloom", 38, 2.2), "Enveloping_Energy")
            motif_image = media(motif, "Existing_Motif")
            paper_images = [motif_image]
            if ident == "akari":
                paper_images += [media(f"char/v3/fx/akari/card_unsent_{k}.png", f"Existing_Letter_{k}") for k in (2,3)]
            for k in range(36):
                a = k * math.tau / 18
                depth = k // 18
                if ident == "akari":
                    radius = f"({.66+depth*.20}-{.62+depth*.20}*{gather}+1.35*{release})"
                    x = f"0.5+cos({a}+{gather}*3.7+{depth}*.25)*{radius}"
                    y = f"0.5+sin({a}+{gather}*3.7+{depth}*.25)*{radius}*1.55"
                    angle = f"{k*25}+{gather}*240+{release}*130"
                    scale = (85 + k%4*24) / mw
                elif ident == "koharu":
                    side = -1 if k % 2 else 1
                    x = f"0.5+{side}*(0.22+{k//6}*0.07)*(1-0.96*{gather}+2.3*{release})"
                    y = f"0.5+(-0.7+{k%18}*0.085)*(1-{gather})+0.95*{release}"
                    angle = f"{side}*(24+{k//6}*9)*(1-{gather}+{release})"
                    scale = (34+k%3*8) / mw
                elif ident == "rei":
                    x = f"0.5+cos({a}+{gather}*1.5+{depth}*.2)*({.5+depth*.18}-{.47+depth*.18}*{gather}+1.1*{release})"
                    y = f"0.5+sin({a}+{gather}*1.5+{depth}*.2)*({.8+depth*.2}-{.77+depth*.2}*{gather}+1.5*{release})"
                    angle = f"{k*30}+{gather}*180+{release}*180"
                    scale = (52 + k % 4 * 22) / mw
                else:
                    x = f"0.5+cos({a}-{gather}*2.6+{depth}*.3)*({.6+depth*.16}-{.57+depth*.16}*{gather}+1.1*{release})"
                    y = f"0.5+sin({a}-{gather}*2.6+{depth}*.3)*({.9+depth*.2}-{.87+depth*.2}*{gather}+1.4*{release})"
                    angle = f"{k*30}+90-{gather}*180+{release}*120"
                    scale = (74+k%3*26) / mw
                layer = merge(layer, paper_images[k%len(paper_images)] if ident == "akari" else motif_image,
                              f"Motif_{k+1:02d}", scale, f"Point({x},{y})", f"{ease(depth*8,12)}*(1-{ease(102,36)})")
                expr(layer, "Angle", angle)
                expr(layer, "Size", f"{scale}*(1+.7*{release})")
            rays = transparent("Transparent_Rays")
            for k in range(48):
                a = k*math.tau/48 + .12
                mask = node("RectangleMask", f"Burst_Ray_{k}")
                mask.Width = .0015 + (k % 4)*.0017
                mask.SoftEdge = .001
                expr(mask, "Height", f"(.13+{k%5}*.055)*(1+.8*{ease(77,5)})*(1-{ease(109,46)})")
                mask.Angle = k*7.5-90+math.degrees(.12)
                radius = f"(.78-.61*{gather}+1.05*{ease(77,37)})"
                expr(mask, "Center", f"Point(0.5+cos({a})*{radius},0.5+sin({a})*{radius}*1.77778)")
                ray = merge(rays, white if k%3 == 0 else accent if k%3 == 1 else light, f"Radiance_{k}",
                            blend=f"(.28*{ease(12,28)}+.72*{ease(74,4)})*(1-{ease(112,40)})")
                ray.EffectMask = mask.Mask
                rays = ray
            layer = merge(layer, glow(rays, "Burst_Bloom", 24, 2.1), "Body_Energy_Release")
            portrait = media(before, "Original_Before")
            old_size = f"{610/bh}*(1+0.085*{gather})"
            old = merge(layer, portrait, "Before_Stationary", 640/bh,
                        blend=f"{ease(0,8)}*iif(time<77,1,0)")
            expr(old, "Size", old_size)
            lit_old = merge(old, silhouette(portrait, "Before_Charged_Silhouette"), "Body_Becomes_Light",
                            blend=f"{ease(43,28)}*iif(time<77,1,0)")
            expr(lit_old, "Size", old_size)
            transformed = media(after, "Original_Transformed")
            size = (630 if ident != "mina" else 650) / ah
            new_size = f"{size}*(1+.065*(1-{ease(83,30)})-.03*min(max((time-128)/51,0),1))"
            reveal = merge(lit_old, transformed, "Transformed_Stationary", size, blend="iif(time>=77,1,0)")
            expr(reveal, "Size", new_size)
            lit_new = merge(reveal, silhouette(transformed, "Transformed_Charged_Silhouette"), "New_Body_Condenses",
                            blend=f"iif(time>=77,1,0)*(1-{ease(81,18)})")
            expr(lit_new, "Size", new_size)
            core = node("EllipseMask", "Transformation_Core")
            expr(core, "Width", f"0.04+1.7*{ease(71,7)}")
            expr(core, "Height", f"0.08+2.5*{ease(71,7)}")
            core.SoftEdge = .12
            pulse = merge(lit_new, white, "Single_Body_Flash",
                          blend=f"{ease(71,6)}*(1-{ease(79,9)})")
            pulse.EffectMask = core.Mask
            finish = transparent("Aftershock_Plate")
            for k in range(4):
                shock = node("EllipseMask", f"Shockwave_{k}")
                shock.Solid, shock.BorderWidth, shock.SoftEdge = 0, .009-k*.0015, .008
                start = 77+k*5
                expr(shock, "Width", f".08+1.9*{ease(start,29+k*3)}")
                expr(shock, "Height", f".14+3.37*{ease(start,29+k*3)}")
                shock_layer = merge(finish, white if k == 0 else light if k%2 else accent, f"Shockwave_Release_{k}",
                                    blend=f"{ease(start,2)}*(1-{ease(start+15,30)})")
                shock_layer.EffectMask = shock.Mask
                finish = shock_layer
            for k in range(42):
                a = k*2.39996
                start = 78+k%7
                drift = ease(start, 80+k%4*8)
                distance = f"(.04+{.42+k%5*.05}*{drift})"
                spark = node("RectangleMask", f"Afterglow_Spark_{k}")
                spark.Width, spark.Height, spark.Angle = .002+k%3*.001, .008+k%4*.005, k*37
                spark.SoftEdge = .002
                expr(spark, "Center", f"Point(.5+cos({a})*{distance},.5+sin({a})*{distance}*1.77778-.11*{drift})")
                point = merge(finish, white if k%3 else accent, f"Spark_{k}",
                              blend=f"{ease(start,9)}*(1-{ease(135+k%9,34)})")
                point.EffectMask = spark.Mask
                finish = point
            output = merge(pulse, glow(finish, "Aftershock_Bloom", 24, 1.8), "Impact_Aftermath")
        finally:
            comp.Unlock()
        item.LoadFusionCompByName(item.GetFusionCompNameList()[0])
        comp.FindTool("MediaOut1").Input = output.Output
        timeline.AddMarker(77, "Yellow", "Transformation impact", "Game BGM and impact sound at 1.28 seconds", 1)
        project.SetCurrentTimeline(timeline)
        timeline.SetCurrentTimecode("00:00:01:55")
        report.append({"id":ident,"timeline":name,"frames":item.GetDuration(),"before":before,"after":after})
    pm.SaveProject()
    return report


result = build(resolve)
