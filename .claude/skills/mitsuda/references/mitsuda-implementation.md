# Godot/.NET 実装レシピ（音を実際に鳴らす）

`mitsuda-style.md` の設計を、このプロジェクト（Godot 4 / .NET / C#）で**実際に鳴る**コードに落とすための手引き。
**コードは提示→承認→Edit**。ここはレシピであり、勝手にコミットしない。行番号は `sound-map.md` 同様、実装前に現物で確認。

---

## A. バス構成（最初の一仕事）
現状は Master のみ。`Settings.cs:58-62` のスライダー（master/bgm/se/voice/amb）を生かすため、対応するバスを作る。

**方法1（推奨・データで持つ）**: Godot エディタでバスを追加 → `default_bus_layout.tres` を作り、`project.godot` の `[audio] default_bus_layout=` に指定。バス：
```
Master
├─ Music   (BGM)
├─ SE      (効果音)
├─ Voice   (テキスト送り音・ボイス)
└─ Amb     (環境音・心象ノイズ)
```
各バスの送り先は Master。`Amb`/`Music` には後段でエフェクト（LowPass 等）を挿す枠を用意（アダプティブ用, §D）。

**方法2（コードで起動時生成）**: オートロードで `AudioServer.AddBus()` → `SetBusName` → `SetBusSend(idx,"Master")` を起動時に行う。`.tres` を編集しない分コードに集約できる。

**`Settings.cs` の配線を拡張**（現状 `case "master"` だけ → 全バスへ）:
```csharp
// src/Settings.cs Apply(Def d) 内（現状 153-156 の case "master" を一般化）
static void SetBusDb(string bus, float f) {
    int i = AudioServer.GetBusIndex(bus);
    if (i >= 0) AudioServer.SetBusVolumeDb(i, f <= 0.5f ? -80f : Mathf.LinearToDb(f / 100f));
}
case "master": SetBusDb("Master", d.F); break;
case "bgm":    SetBusDb("Music",  d.F); break;
case "se":     SetBusDb("SE",     d.F); break;
case "voice":  SetBusDb("Voice",  d.F); break;
case "amb":    SetBusDb("Amb",    d.F); break;
```
これで「鳴らない4スライダー」が生きる。`ApplyAll()`（:148）が起動時に全項目へ Apply するので、保存値も反映される。

---

## B. SE 再生の基盤（Audio オートロード）
全所から呼べる軽量な SE 再生器を1つ用意し、`FxLayer.Instance` と同じパターン（静的 Instance）で常駐させる。

```csharp
// src/Audio.cs（新規）— FxLayer と同じ「Mainがworldに1個追加, Instance経由で呼ぶ」流儀
public partial class Audio : Node
{
    public static Audio Instance;
    private readonly List<AudioStreamPlayer> _sePool = new();
    private AudioStreamPlayer _music;
    public bool Muted;   // デモ/QA用（§F）

    public override void _Ready() {
        Instance = this;
        for (int i = 0; i < 12; i++) {                 // SE 同時発音プール
            var p = new AudioStreamPlayer { Bus = "SE" };
            AddChild(p); _sePool.Add(p);
        }
        _music = new AudioStreamPlayer { Bus = "Music" }; AddChild(_music);
    }

    public void Se(AudioStream s, float volDb = 0f, float pitch = 1f) {
        if (Muted || s == null) return;
        var p = _sePool.Find(x => !x.Playing) ?? _sePool[0];  // 空きを使う＝同時発音制限
        p.Stream = s; p.VolumeDb = volDb; p.PitchScale = pitch; p.Play();
    }

    public void Music(AudioStream s, bool loop = true) { /* クロスフェードは §C */ }
}
```
- **同時発音制限**（style §5）: プール枯渇時は最古を奪うだけ＝弾幕で SE が飽和しても破綻しない。`QaPilot` の 1200 発でも安全。
- **ピッチ揺らぎ**: 連射SEは `pitch = R(0.97,1.03)` で機械反復を避ける。Overload 中は `pitch += 0.15` 等で高揚（`GameManager.IsOverload`）。

**SEトリガの挿入は `FxLayer` 呼び出しに相乗り**（style §4）。例：
```csharp
// src/Enemy.cs Redeem() 内、FxLayer.Instance.PurifyBurst(pos) の隣に
Audio.Instance?.Se(SfxPurify, volDb: -2f);
// src/Player.cs Fire() の FxLayer.Instance.Muzzle(muzzlePos) の隣に
Audio.Instance?.Se(SfxShot, volDb: -8f, pitch: 0.98f + 0.04f*frac);
// src/Player.cs TakeHit() の PlayerHit() の隣に（最優先・ダッキング §E）
Audio.Instance?.Se(SfxHit, volDb: 0f);
```
音源は `[Export] AudioStream SfxShot;` 等で各シーンに割るか、`Audio` 内で `GD.Load<AudioStream>("res://audio/se/shot.ogg")` で集中管理。

---

## C. BGM 再生とループ・遷移
- **ループ**: `.ogg` は `AudioStreamOggVorbis` の `Loop`/`LoopOffset` を設定（イントロ→ループ本体。2周目はイントロ省略, style §9）。インポート時に設定するか、コードで `stream.Loop = true`。
- **シーン遷移のクロスフェード**: 旧 `_music` を Tween で `-80db` へ、新 player を立ち上げて同時に上げる。ブツ切り禁止（style §6・§10）。共通主題で橋を架けると尚良い。
```csharp
// クロスフェード例（Audio 内）
var old = _music; var nw = new AudioStreamPlayer { Bus = "Music", Stream = s, VolumeDb = -80 };
AddChild(nw); nw.Play();
var t = CreateTween().SetParallel();
t.TweenProperty(nw, "volume_db", 0f, 1.0);
t.TweenProperty(old, "volume_db", -80f, 1.0);
t.Chain().TweenCallback(Callable.From(() => old.QueueFree()));
_music = nw;
```
- シーン遷移は各 `*.tscn` の `_Ready` で `Audio.Instance.Music(theme)` を呼ぶ。Prologue/Final/Epilogue は独自レンダラなので、そのフェーズ遷移（例 `Epilogue.cs` の `phase`）に合わせて切替。

---

## D. アダプティブ音楽（汚染ゲージで濁す）
連続値＝フィルタ、離散＝レイヤー/分岐（style §6）。

**濁し（Contamination → LowPass / ピッチ / ノイズパッド）**:
- `Music` バスに `AudioEffectLowPassFilter` を1枚挿し、毎フレーム `Contamination` でカットオフを動かす：
```csharp
// Final/GameManager の update で
int mb = AudioServer.GetBusIndex("Music");
var lp = (AudioEffectLowPassFilter)AudioServer.GetBusEffect(mb, 0);
lp.CutoffHz = Mathf.Lerp(800f, 16000f, 1f - contamination);  // 汚染↑でこもる
```
- 「濁りパッド」を `Amb` バスで常時ループさせ、`contamination` で音量を上げる（ノイズ/不協和）。視覚の `Player Tint`（`Player.cs:118-121`）と**同じ曲線**で動かすと画と音が一致。

**レイヤー（ボス段階移行 → 楽器を足す）**:
- 同尺・同BPMの複数 `.ogg`（リズム層・旋律層・緊張層）を同時再生し、段階で層の音量を上げ下げ。`BossRei.OnHpChanged()`（:169付近）で発火。位相を揃えるため**同時に Play して音量で抜き差し**する（後から Play しない）。

---

## E. ミックスと優先度（重要音を埋もれさせない）
- **被弾・残機・段階移行は最優先**（style §5）。これらが鳴る瞬間、`SE` バスを一瞬下げる（ダッキング）か、専用の高優先バスに分ける。
- 簡易ダッキング: 警告音を鳴らす直前に `SE` バスを `-6db` へ Tween し、0.3s で戻す。または警告音だけ `Master` 直下の別バス `Alert` に逃がす。
- **会話中はSE抑制**（`Hud.cs:93-96 BubblePaused`）。`Audio.Muted` ではなく `SE` バスを下げる/止めることで、ボイスとBGMは残す。
- `Settings` の `voice`/`amb` を尊重。テキスト送り音は `Voice` バス（style §11 会話同期）。

---

## F. デモ/QA でのミュート（無音前提を壊さない）
- `QaPilot`（`--qa`）は高速・無音で回る。`Audio.Instance.Muted = true` を `QaPilot._Ready` 相当で立てる。SE プールは `Muted` で即 return（§B）。
- `DemoPilot`（`--demo`）は通常**無音**だが、`demo-video` 収録時は鳴らしたい。引数で分岐：収録モードのみ `Muted=false`。BGM/SE が乗ればプロモの説得力が上がる（`/sakurai` 連携）。
- これらは `sound-map.md` §6 と対応。自動プレイのテンポを音処理で落とさないこと。

---

## G. 音源が未調達のときの暫定運用
本制作の実音源が無い間も、**結線と体験を先に通す**：
- **合成SE**: `AudioStreamGenerator` か、短い手書きエンベロープのトーンで暫定。発射＝短い矩形/ノイズ、浄化＝減衰ベル風サイン、被弾＝低いノイズバースト。差し替え前提でファイル名を確定（`res://audio/se/shot.ogg` 等）。
- **プレースホルダBGM**: ロイヤリティフリー/自作ループを仮置きし、ループ点と遷移だけ詰める。主題が決まれば差し替え。
- **差し替えポイントを明示**: `Audio` の `GD.Load` パスを一覧化し、「ここに正式音源を置けば鳴る」状態にする。**「作曲した」と偽らない**（SKILL「できること」節）。

---

## H. 実装チェックリスト
- [ ] バス（Music/SE/Voice/Amb）を作り、`Settings.Apply` を全バスへ配線した（§A）
- [ ] `Audio` オートロードを `FxLayer` と同じ流儀で常駐させた（§B）
- [ ] SEトリガを `FxLayer.*` 呼び出しの隣に置き、画と音が同フレームになっている（§B, style §4）
- [ ] 同時発音制限・ピッチ揺らぎを入れた（弾幕で飽和しない／機械反復しない）（§B）
- [ ] BGM はループ点・クロスフェードを詰めた（ブツ切り無し）（§C）
- [ ] 汚染/Warmth を LowPass・レイヤーで音に反映（連続=フィルタ/離散=レイヤー）（§D）
- [ ] 被弾等の警告音が最優先・ダッキングで埋もれない（§E）
- [ ] 会話中のSE抑制／`Settings` の voice/amb/msg/auto を尊重（§E, style §11）
- [ ] QA は Muted、デモ収録のみ鳴らす（§F）
- [ ] 未調達音源は暫定音で結線し差し替えポイントを明示（§G）
- [ ] `play-game`/`demo-video` で実際に聴いて確かめた（style §12）
