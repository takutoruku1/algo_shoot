using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

public partial class BossTechniqueVideo
{
    private int _presencePortraitChecks, _presenceFadeChecks;
    private Texture2D? _presenceHumanTexture;
    private static readonly System.Reflection.MethodInfo PresenceSpriteFaceMethod = typeof(BossIllustrationMotion)
        .GetMethod("FaceClearance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
            null, new[] { typeof(Sprite2D) }, null)!;

    private void PresenceDamageEnabled()
    {
        _player.AddLife(10);
        Write(_player, "_invincible", false);
        Write(_player, "_invincibleTimer", 0f);
        Write(_player, "_hitInvincible", false);
        Write(_player, "_dodgeInv", 0f);
        Write(_player, "_veilT", 0f);
        EasyRequire(!_game.TutorialNoConsume && !QaPilot.GodActive, "Presence QA requires real damage.");
    }

    private Vector2 PresenceBodyCenter(Enemy enemy)
    {
        var body = Read<CollisionShape2D>(enemy, "_bodyShape");
        return body.GlobalTransform * body.Shape.GetRect().GetCenter();
    }

    private static int PresenceDepth(CanvasItem item)
    {
        int depth = item.ZIndex;
        while (item.ZAsRelative && item.GetParent() is CanvasItem parent)
        {
            depth += parent.ZIndex;
            item = parent;
        }
        return depth;
    }

    private void PresenceRenderOrder()
    {
        var frame = _boss.GetNode<CanvasItem>("BossBattleFrame");
        var body = Read<Sprite2D>(_boss, "_bodySprite");
        EasyRequire(!frame.ZAsRelative && frame.IsVisibleInTree() && PresenceDepth(body) > PresenceDepth(frame),
            "The boss frame does not separate the portrait from the attack drawing layer.");
        EasyRequire(_boss.GetChildren().OfType<Panel>().All(panel => PresenceDepth(panel) > PresenceDepth(frame)),
            "An unfolder is hidden behind the boss frame.");
    }

    private void PresenceFrameState(bool active, string stage)
    {
        var frame = _boss.GetNode<CanvasItem>("BossBattleFrame");
        EasyRequire(Field.BossFrameActive == active
            && Field.Right == (active ? Field.BossFrameLeft : Field.WorldRight)
            && frame.IsVisibleInTree() == active,
            $"The boss frame and playable boundary disagree: stage={stage} expected_frame={active} active={Field.BossFrameActive} visible={frame.IsVisibleInTree()} right={Field.Right}.");
        PresencePortraitGeometry(stage);
    }

    private Rect2 PresencePortraitSprite(Sprite2D sprite, string stage)
    {
        var material = sprite.Material as ShaderMaterial;
        EasyRequire(material != null && !material.Shader.Code.Contains("frame_fit")
            && material.Shader.Code.Contains("frame_offset") && material.Shader.Code.Contains("portrait_clip"),
            "The portrait shader still scales the illustration to fit its frame: " + stage);
        bool framed = (Field.BossFrameActive || _boss.BossFrameReturning) && !_boss.BossInArena;
        var frame = Field.BossFrameRect;
        var clip = (Vector4)sprite.GetInstanceShaderParameter("portrait_clip");
        var expectedClip = framed ? new Vector4(frame.Position.X, frame.Position.Y, frame.End.X, frame.End.Y) : Vector4.Zero;
        var offset = (Vector2)sprite.GetInstanceShaderParameter("frame_offset");
        EasyRequire(clip.IsEqualApprox(expectedClip) && offset.IsFinite() && (framed || offset == Vector2.Zero),
            $"Portrait clipping disagrees with the current frame: stage={stage} clip={clip} expected={expectedClip} offset={offset}.");
        Rect2 face = (Rect2)PresenceSpriteFaceMethod.Invoke(null, new object[] { sprite })!;
        Rect2 originalFace;
        try
        {
            sprite.SetInstanceShaderParameter("frame_offset", Vector2.Zero);
            originalFace = (Rect2)PresenceSpriteFaceMethod.Invoke(null, new object[] { sprite })!;
        }
        finally
        {
            sprite.SetInstanceShaderParameter("frame_offset", offset);
        }
        EasyRequire(face.Size.DistanceTo(originalFace.Size) < .02f,
            $"Moving the portrait into its frame changed the face size: stage={stage} original={originalFace.Size} framed={face.Size}.");
        if (framed)
        {
            var visibleFace = face.Intersection(frame);
            EasyRequire(frame.HasPoint(face.GetCenter())
                && visibleFace.Size.X >= Math.Min(face.Size.X, frame.Size.X - 8) - .02f
                && visibleFace.Size.Y >= Math.Min(face.Size.Y, frame.Size.Y - 8) - .02f,
                $"The unscaled portrait face is cropped out of its frame: stage={stage} face={face} frame={frame}.");
        }
        var transform = sprite.GlobalTransform;
        var scale = sprite.GlobalScale;
        BossIllustrationMotion.FitToFrame(sprite, _boss);
        EasyRequire(sprite.GlobalTransform == transform && sprite.GlobalScale == scale,
            "Frame placement changed the portrait's native transform or scale: " + stage);
        return face;
    }

    private void PresencePortraitGeometry(string stage)
    {
        var body = _boss.GetNode<Sprite2D>("Body");
        var scale = body.GlobalScale;
        body.GetNode<BossReadabilitySprite>("BossReadability").Sync();
        EasyRequire(body.GlobalScale == scale, "Portrait synchronization changed its native scale: " + stage);
        PresencePortraitSprite(body, stage);
        _presencePortraitChecks++;
    }

    private bool PresenceArtFacesRight(Texture2D texture)
        => _boss is not BossMina && !texture.ResourcePath.Contains("/animal_forms/")
            && !texture.ResourcePath.Contains("_animal_breath_")
            && !texture.ResourcePath.Contains("/mina_dragon/");

    private void PresenceFacingSprites(bool faceLeft, string stage)
    {
        var body = _boss.GetNode<Sprite2D>("Body");
        body.GetNode<BossReadabilitySprite>("BossReadability").Sync();
        foreach (var sprite in new[] { body, _boss.FadingBody }.OfType<Sprite2D>())
        {
            bool artRight = PresenceArtFacesRight(sprite.Texture);
            bool expectedFlip = faceLeft == artRight;
            EasyRequire(sprite.FlipH == expectedFlip,
                $"Portrait faces the wrong side: stage={stage} texture={sprite.Texture.ResourcePath} art_faces_right={artRight} wanted_left={faceLeft} expected_flip={expectedFlip} actual_flip={sprite.FlipH} outgoing={sprite != body}.");
            EasyRequire((float)((ShaderMaterial)sprite.Material).GetShaderParameter("facing") == (expectedFlip ? -1f : 1f),
                "The portrait shader does not match its semantic facing: " + stage);
        }
    }

    private void PresenceFacingQa(int phase)
    {
        PresenceFacingSprites(true, "phase entry");
        _boss.FinishFormReveal();
        var body = _boss.GetNode<Sprite2D>("Body");
        var texture = body.Texture;
        if (phase == 0) _presenceHumanTexture = texture;
        var counterpart = _boss is BossMina
            ? phase < BossMina.DragonPhase ? GD.Load<Texture2D>(BossMina.BattleCostumePath(BossMina.DragonPhase, "idle"))
                : _presenceHumanTexture!
            : PresenceArtFacesRight(texture) ? GD.Load<Texture2D>(BossAnimalArt.Path(_scene.ToLowerInvariant(), "idle"))
                : _presenceHumanTexture!;
        EasyRequire(texture != counterpart && (_boss is BossMina
            ? !PresenceArtFacesRight(texture) && !PresenceArtFacesRight(counterpart)
            : PresenceArtFacesRight(texture) != PresenceArtFacesRight(counterpart)),
            "The facing fixture did not select the character's required human and transformed illustrations.");
        var material = body.Material;
        var transform = body.Transform;
        var offset = body.Offset;
        var position = _boss.GlobalPosition;
        var playerPosition = _player.GlobalPosition;
        var motionOffset = Read<Vector2>(_boss, "_motionOffset");
        float baseScale = Read<float>(_boss, "_baseScale");
        var mover = Read<BossMover>(_boss, "_mover");
        bool moverFacingLeft = mover.FacingLeft;
        bool originalLeft = body.FlipH == PresenceArtFacesRight(texture);
        int swaps = 0, motionChanges = 0;
        try
        {
            foreach (bool left in new[] { false, true })
            {
                Call(_boss, "SetFacingLeft", left);
                PresenceFacingSprites(left, "explicit facing");
                Call(_boss, "SwapBody", counterpart.ResourcePath, Read<float>(_boss, "_bodyScaleMul"));
                PresenceFacingSprites(left, "transformed-art swap");
                Call(_boss, "SetFacingLeft", !left);
                PresenceFacingSprites(!left, "transformed-art fade turn");
                Call(_boss, "SetFacingLeft", left);
                Call(_boss, "SwapBody", texture.ResourcePath, Read<float>(_boss, "_bodyScaleMul"));
                PresenceFacingSprites(left, "original-art swap");
                _boss.FinishFormReveal();
                swaps += 2;
                Call(_boss, "SetMotionFrame", counterpart);
                PresenceFacingSprites(left, "transformed-art motion frame");
                Call(_boss, "SetMotionFrame", texture);
                PresenceFacingSprites(left, "original-art motion frame");
                motionChanges += 2;
            }
            _player.GlobalPosition = new Vector2(Field.Left + 24, Field.CenterY);
            Write(mover, "<FacingLeft>k__BackingField", false);
            Call(_boss, "UpdateMovement", 0d);
            EasyRequire(!mover.FacingLeft, "The framed-facing fixture lost its right-facing mover state.");
            PresenceFacingSprites(true, "framed movement overrides right-facing mover");
        }
        finally
        {
            _boss.FinishFormReveal();
            Call(_boss, "SetMotionFrame", texture);
            Call(_boss, "SetFacingLeft", originalLeft);
            body.Material = material;
            body.Transform = transform;
            body.Offset = offset;
            Write(_boss, "_baseScale", baseScale);
            Write(_boss, "_motionOffset", motionOffset);
            Write(mover, "<FacingLeft>k__BackingField", moverFacingLeft);
            _boss.GlobalPosition = position;
            _player.GlobalPosition = playerPosition;
            body.GetNode<BossReadabilitySprite>("BossReadability").Sync();
        }
        GD.Print($"[BOSS-PRESENCE] FACING phase={phase} both_directions=true texture_swaps={swaps} motion_frame_changes={motionChanges} fade_direction=true real_movement_dispatch=true framed_left_priority=true texture_fixture=true");
    }

    private bool PresenceHoldingFacingQa(int phase)
    {
        bool chasing = _boss is BossKoharu && phase == 1 || _boss is BossMina && phase >= BossMina.DragonPhase;
        if (!chasing) return false;
        var body = _boss.GetNode<Sprite2D>("Body");
        var texture = body.Texture;
        var material = body.Material;
        var transform = body.Transform;
        var offset = body.Offset;
        var position = _boss.GlobalPosition;
        var playerPosition = _player.GlobalPosition;
        var motionOffset = Read<Vector2>(_boss, "_motionOffset");
        float baseScale = Read<float>(_boss, "_baseScale");
        var mover = Read<BossMover>(_boss, "_mover");
        bool moverFacingLeft = mover.FacingLeft, flipWantLeft = Read<bool>(mover, "_flipWantLeft");
        double flipWant = Read<double>(mover, "_flipWant"), flipSquash = Read<double>(mover, "_flipSquashT");
        int flipCount = mover.FlipCount;
        BossCharacterTechnique? attack = null;
        try
        {
            attack = BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Extra, _boss is BossMina ? 1 : 0);
            attack.SetPhysicsProcess(false);
            attack.SetProcess(false);
            EasyRequire(_boss.BossInArena && Read<BossDanmaku>(_boss, "_danmaku").Holding,
                "The facing fixture did not enter a real chasing technique's movement hold.");
            _boss.GlobalPosition = new Vector2(Field.CenterX, Field.CenterY);
            var heldPosition = _boss.GlobalPosition;
            Write(mover, "<FacingLeft>k__BackingField", true);
            Write(mover, "_flipWant", 0d);
            foreach (bool left in new[] { false, true, false })
            {
                _player.GlobalPosition = heldPosition + new Vector2(left ? -70 : 70, 0);
                for (int frame = 0; frame < 45; frame++)
                {
                    Call(_boss, "UpdateMovement", 1d / 60);
                    EasyRequire(_boss.GlobalPosition.DistanceTo(heldPosition) < .01f,
                        "The held movement-facing update moved the chasing boss.");
                    if (frame < 30) EasyRequire(mover.FacingLeft != left,
                        "A held chasing boss turned before the existing facing hold elapsed.");
                }
                EasyRequire(mover.FacingLeft == left, "A held chasing boss did not turn toward the player.");
                PresenceFacingSprites(left, "chase movement hold");
                _player.GlobalPosition = heldPosition + new Vector2(left ? 20 : -20, 0);
                for (int frame = 0; frame < 12; frame++) Call(_boss, "UpdateMovement", 1d / 60);
                EasyRequire(mover.FacingLeft == left, "The chasing boss flickered inside its facing deadzone.");
                PresenceFacingSprites(left, "chase facing deadzone");
            }
            attack.Cancel();
            EasyRequire(!_boss.BossInArena && _boss.BossFrameReturning && !mover.FacingLeft,
                "The facing fixture did not cancel a right-facing chase into frame return.");
            PresenceFacingSprites(true, "chase cancellation immediate facing");
            for (int frame = 0; frame < 72; frame++)
            {
                _boss.TickBossFrame(1d / 60);
                _player.GlobalPosition = new Vector2(Field.Right - Player.HitRadius, Field.CenterY);
                Call(_boss, "UpdateMovement", 0d);
                EasyRequire(!mover.FacingLeft, "The return fixture no longer exercises the right-facing deadzone.");
                PresenceFacingSprites(true, "frame return overrides facing deadzone");
            }
            EasyRequire(!_boss.BossFrameReturning && Field.Right == Field.BossFrameLeft,
                "The short facing fixture did not finish its frame return.");
        }
        finally
        {
            attack?.Cancel();
            if (_boss.BossFrameReturning) _boss.TickBossFrame(1.2);
            _boss.FinishFormReveal();
            Call(_boss, "SetMotionFrame", texture);
            Call(_boss, "SetFacingLeft", true);
            body.Material = material;
            body.Transform = transform;
            body.Offset = offset;
            Write(_boss, "_baseScale", baseScale);
            Write(_boss, "_motionOffset", motionOffset);
            Write(mover, "<FacingLeft>k__BackingField", moverFacingLeft);
            Write(mover, "_flipWantLeft", flipWantLeft);
            Write(mover, "_flipWant", flipWant);
            Write(mover, "_flipSquashT", flipSquash);
            Write(mover, "_flipCount", flipCount);
            _boss.GlobalPosition = position;
            _player.GlobalPosition = playerPosition;
            mover.SetPlayerPos(playerPosition);
            body.GetNode<BossReadabilitySprite>("BossReadability").Sync();
        }
        GD.Print($"[BOSS-PRESENCE] HOLDING_FACING phase={phase} actual_chase=true actual_movement_dispatch=true turns=3 hold_seconds=.6 deadzone_preserved=true return_left_priority=true fixture_positioning=true");
        return true;
    }

    private void PresencePortraitFade(int phase)
    {
        _boss.FinishFormReveal();
        var body = _boss.GetNode<Sprite2D>("Body");
        var material = body.Material;
        var transform = body.Transform;
        var scale = body.GlobalScale;
        try
        {
            Call(_boss, "SwapBody", body.Texture.ResourcePath, Read<float>(_boss, "_bodyScaleMul"));
            Call(_boss, "TickSwapAnim", .05d);
            PresencePortraitGeometry("fade body");
            var fading = _boss.FadingBody;
            EasyRequire(fading != null && fading.IsVisibleInTree() && fading.SelfModulate.A > 0,
                "The portrait fixture did not exercise a visible outgoing illustration.");
            PresencePortraitSprite(fading!, "fade outgoing");
            EasyRequire(body.GlobalScale.IsEqualApprox(scale) && fading!.GlobalScale.IsEqualApprox(scale),
                "Entering a portrait fade changed the illustration scale.");
            var outgoing = fading!;
            var offset = (Vector2)outgoing.GetInstanceShaderParameter("frame_offset");
            var clip = (Vector4)outgoing.GetInstanceShaderParameter("portrait_clip");
            outgoing.SetInstanceShaderParameter("frame_offset", new Vector2(999, -999));
            outgoing.SetInstanceShaderParameter("portrait_clip", Vector4.Zero);
            var readability = body.GetNode<BossReadabilitySprite>("BossReadability");
            readability.Sync();
            EasyRequire(((Vector2)outgoing.GetInstanceShaderParameter("frame_offset")).IsEqualApprox(offset)
                && ((Vector4)outgoing.GetInstanceShaderParameter("portrait_clip")).IsEqualApprox(clip),
                "Draw synchronization did not repair stale clipping on the outgoing portrait.");
            bool bodyFlip = body.FlipH, outgoingFlip = outgoing.FlipH;
            try
            {
                body.FlipH = !bodyFlip;
                outgoing.FlipH = !outgoingFlip;
                ((ShaderMaterial)body.Material).SetShaderParameter("facing", 0f);
                ((ShaderMaterial)outgoing.Material).SetShaderParameter("facing", 0f);
                readability.Sync();
                EasyRequire((float)((ShaderMaterial)body.Material).GetShaderParameter("facing") == (body.FlipH ? -1f : 1f)
                    && (float)((ShaderMaterial)outgoing.Material).GetShaderParameter("facing") == (outgoing.FlipH ? -1f : 1f),
                    "Draw synchronization did not match both portrait shaders to the flipped sprites.");
                PresencePortraitSprite(body, "flipped fade body");
                PresencePortraitSprite(outgoing, "flipped fade outgoing");
            }
            finally
            {
                body.FlipH = bodyFlip;
                outgoing.FlipH = outgoingFlip;
                readability.Sync();
            }
            _presenceFadeChecks++;
        }
        finally
        {
            _boss.FinishFormReveal();
            body.Material = material;
            body.Transform = transform;
            body.GetNode<BossReadabilitySprite>("BossReadability").Sync();
        }
        GD.Print($"[BOSS-PRESENCE] PORTRAIT_FADE phase={phase} scale={scale} native_scale_preserved=true clipped=true stale_clip_repaired=true flipped_facing_synchronized=true same_texture_swap_fixture=true");
    }

    private (int Bounds, int Links) PresenceFaceClearance(string stage)
    {
        PresencePortraitGeometry(stage);
        Rect2 face = BossIllustrationMotion.FaceClearance(_boss);
        bool framedOrbit = (Field.BossFrameActive || _boss.BossFrameReturning) && !_boss.BossInArena;
        var occupied = new System.Collections.Generic.List<Rect2>();
        int boundsChecked = 0, linksChecked = 0;
        foreach (var panel in _boss.GetChildren().OfType<Panel>())
        {
            var bounds = panel.VisualBounds;
            for (int index = 0; index < bounds.Length; index++)
            {
                var bound = bounds[index];
                EasyRequire(framedOrbit && index < 2 || !bound.Grow(-.02f).Intersects(face),
                    $"An unfolder covers the boss face: stage={stage} panel={panel.Name} face={face} visual={bound}.");
                boundsChecked++;
            }
            if (bounds.Length >= 2)
            {
                var core = bounds[0].Merge(bounds[1]);
                EasyRequire(framedOrbit || occupied.All(other => !core.Grow(-.02f).Intersects(other)),
                    "Unfolder bodies or HP marks overlap: " + stage);
                if (framedOrbit && !_boss.BossFrameReturning)
                    EasyRequire(Field.BossFrameRect.Encloses(core.Grow(-.02f)), "An orbiting shield left the boss frame: " + stage);
                occupied.Add(core);
            }
            foreach (var link in panel.VisualLinks)
            {
                bool crosses = face.HasPoint(link.From) || face.HasPoint(link.To);
                var corners = new[] { face.Position, new Vector2(face.End.X, face.Position.Y),
                    face.End, new Vector2(face.Position.X, face.End.Y) };
                var direction = link.To - link.From;
                for (int edge = 0; edge < 4 && !crosses; edge++)
                {
                    var side = corners[(edge + 1) % 4] - corners[edge];
                    float denominator = direction.Cross(side);
                    if (Math.Abs(denominator) < .00001f) continue;
                    var offset = corners[edge] - link.From;
                    float t = offset.Cross(side) / denominator, u = offset.Cross(direction) / denominator;
                    crosses = t >= 0 && t <= 1 && u >= 0 && u <= 1;
                }
                EasyRequire(!crosses, "An unfolder warp line crosses the boss face: " + stage);
                linksChecked++;
            }
        }
        return (boundsChecked, linksChecked);
    }

    private void PresenceFaceOrbit(int phase)
    {
        var panels = _boss.GetChildren().OfType<Panel>().Where(panel => !panel.RallyDriven).ToArray();
        var saved = panels.Select(panel => (Panel: panel, panel.Invulnerable, Time: Read<float>(panel, "_motionTime"))).ToArray();
        Vector2 position = _boss.GlobalPosition;
        int boundsChecked = 0, linksChecked = 0;
        try
        {
            foreach (var panel in panels) panel.Invulnerable = false;
            for (int sample = 0; sample < 96; sample++)
            {
                _boss.GlobalPosition = new Vector2(Field.BossFrameCenterX, 48 + (Field.Height - 96) * (sample % 32) / 31f);
                foreach (var panel in panels) Write(panel, "_motionTime", sample * .2f);
                panels[0]._PhysicsProcess(0);
                var counts = PresenceFaceClearance($"orbit phase={phase} sample={sample}");
                boundsChecked += counts.Bounds;
                linksChecked += counts.Links;
            }
        }
        finally
        {
            _boss.GlobalPosition = position;
            foreach (var value in saved)
            {
                value.Panel.Invulnerable = value.Invulnerable;
                Write(value.Panel, "_motionTime", value.Time);
            }
            panels[0]._PhysicsProcess(0);
        }
        EasyRequire(boundsChecked > 0 && (_boss is not BossMina || phase < 3 || linksChecked > 0),
            "The face geometry fixture did not exercise visible unfolders and dragon warp links.");
        GD.Print($"[BOSS-PRESENCE] FACE phase={phase} samples=96 bounds={boundsChecked} warp_links={linksChecked} intersections=0 time_position_fixture=true");
    }

    private void PresenceVerticalPatrol(int phase)
    {
        if (phase != 0) return;
        var mover = Read<BossMover>(_boss, "_mover");
        Vector2 savedPosition = _boss.GlobalPosition;
        float minimumY = Field.CenterY, maximumY = Field.CenterY;
        int priorTarget = Read<int>(mover, "_routeIndex"), changes = 0;
        try
        {
            _boss.GlobalPosition = new Vector2(Field.BossFrameCenterX, Field.CenterY);
            for (int frame = 0; frame < 1200; frame++)
            {
                _boss.GlobalPosition = mover.Step(_boss.GlobalPosition, 1d / 60, framed: true);
                EasyRequire(_boss.GlobalPosition.X == Field.BossFrameCenterX
                    && _boss.GlobalPosition.Y >= 48 && _boss.GlobalPosition.Y <= Field.Bottom - 48,
                    "The boss patrol left its reserved vertical frame bounds.");
                minimumY = Math.Min(minimumY, _boss.GlobalPosition.Y);
                maximumY = Math.Max(maximumY, _boss.GlobalPosition.Y);
                int target = Read<int>(mover, "_routeIndex");
                if (target != priorTarget) changes++;
                priorTarget = target;
            }
        }
        finally
        {
            _boss.GlobalPosition = savedPosition;
        }
        EasyRequire(changes >= 3 && maximumY - minimumY > 60,
            $"The framed boss stopped at its initial patrol target: changes={changes} y={minimumY}..{maximumY}.");
        GD.Print($"[BOSS-PRESENCE] PATROL boss={_scene} frames=1200 target_changes={changes} minimum_y={minimumY} maximum_y={maximumY} actual_boss_mover=true direct_step_fixture=true");
    }

    private void PresenceLockTarget(bool expectUnfolder)
    {
        foreach (bool pressed in new[] { false, true })
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, PhysicalKeycode = Key.Shift, Pressed = pressed });
            Input.FlushBufferedEvents();
            Pad.ReleaseMouseFollow();
            _player._PhysicsProcess(1d / 60);
        }
        EasyRequire(_player.LockedOn && _player.LockTarget == _boss
            && (expectUnfolder ? _player.LockedUnfolder?.GetParent() == _boss : _player.LockedUnfolder == null),
            "Shift input cannot acquire the boss and its current target inside the reserved frame.");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, PhysicalKeycode = Key.Shift, Pressed = false });
        Input.FlushBufferedEvents();
        _player._PhysicsProcess(1d / 60);
        EasyRequire(!_player.LockedOn && !_player.LockArmed, "Releasing Shift did not release the framed boss lock.");
    }

    private async Task PresencePlayerBoundary(string stage, bool frameActive = true)
    {
        PresenceFrameState(frameActive, stage);
        _player.GlobalPosition = new Vector2(Math.Min(Field.Right - 40, Field.BossFrameLeft - 24), Field.CenterY);
        PresenceDamageEnabled();
        int lives = _player.Lives;
        float maxStep = 0;
        for (int frame = 0; frame < 120; frame++)
        {
            Vector2 previous = _player.GlobalPosition;
            AutoplayInput.Move(Vector2.Right);
            Input.FlushBufferedEvents();
            Pad.ReleaseMouseFollow();
            _player._PhysicsProcess(1d / 60);
            maxStep = Math.Max(maxStep, previous.DistanceTo(_player.GlobalPosition));
            EasyRequire(_player.GlobalPosition.X <= Field.Right - Player.HitRadius + .01f,
                "Normal movement crossed the current playable boundary: " + stage);
            await EasyPhysicsFrames(1);
        }
        _pilot.Release();
        EasyRequire(_player.GlobalPosition.X > Field.Right - Player.HitRadius - .1f && _player.Lives == lives
            && maxStep <= _player.MoveSpeed / 60 + .01f,
            "Normal movement did not reach the playable boundary safely: " + stage);
        GD.Print($"[BOSS-PRESENCE] PLAYER stage={stage} frame_active={frameActive} right={Field.Right} stopped_x={_player.GlobalPosition.X} max_step={maxStep} normal_input=true invincible=false");
    }

    private async Task PresenceFrameReturn(string stage, Vector2 playerBeforeFinish, BossAttackCycle.Slot? deferredSlot = null)
    {
        bool pausedReturn = stage == "deferred chase cancellation";
        bool previousPause = Hud.BubblePaused;
        if (pausedReturn) Hud.BubblePaused = true;
        EasyRequire(_boss.BossFrameReturning && !_boss.BossInArena
            && _player.GlobalPosition.DistanceTo(playerBeforeFinish) < .01f
            && Field.Right == Field.WorldRight,
            "Ending the chase teleported the player or skipped the frame return: " + stage);
        BossCharacterTechnique? deferred = deferredSlot is { } pending
            ? BossCharacterTechnique.Begin(_boss, pending.Kind, pending.Variant) : null;
        deferred?.SetPhysicsProcess(false);
        deferred?.SetProcess(false);
        float maximumStep = 0;
        int frames = 0;
        for (; frames < 120 && _boss.BossFrameReturning; frames++)
        {
            float beforeRight = Field.Right;
            Vector2 beforeBoss = _boss.GlobalPosition;
            Vector2 before = _player.GlobalPosition;
            if (frames < 30)
            {
                double phaseTime = Read<double>(_boss, "_phaseT");
                float shieldTime = Read<float>(_boss, "_shieldTime");
                _boss._PhysicsProcess(1d / 60);
                EasyRequire(_boss.BossFrameReturning && Read<double>(_boss, "_phaseT") == phaseTime
                    && Read<float>(_boss, "_shieldTime") == shieldTime,
                    "The real boss physics consumed phase or shield time while its frame was returning.");
            }
            else _boss.TickBossFrame(1d / 60);
            if (deferred != null && _boss.BossFrameReturning)
            {
                deferred.Tick(1d / 60);
                EasyRequire(deferred.Elapsed == 0 && deferred.SpellName == "" && !deferred.IsStriking
                    && !deferred.OwnedShots.Any() && !_boss.BossInArena,
                    "A queued chasing technique started before the frame return completed.");
            }
            AutoplayInput.Move(Vector2.Right);
            Input.FlushBufferedEvents();
            Pad.ReleaseMouseFollow();
            _player._PhysicsProcess(1d / 60);
            maximumStep = Math.Max(maximumStep, before.DistanceTo(_player.GlobalPosition));
            float boundaryStep = beforeRight - Field.Right;
            EasyRequire(boundaryStep >= -.001f && boundaryStep <= Field.BossFrameWidth / 1.2f / 60 + .01f,
                "The returning frame changed its playable boundary discontinuously.");
            EasyRequire(_player.GlobalPosition.X <= Field.Right - Player.HitRadius + .01f
                && maximumStep <= _player.MoveSpeed / 60 + .01f,
                "The returning frame pushed the player by more than one normal movement step.");
            await EasyPhysicsFrames(1);
            EasyRequire(Field.BossFrameActive && _boss.GetNode<CanvasItem>("BossBattleFrame").IsVisibleInTree()
                && Field.BossFrameRect.Position.X == Field.Right
                && _boss.GlobalPosition.DistanceTo(beforeBoss) < 5,
                "The returning frame, boss and playable boundary are not synchronized.");
            PresenceFaceClearance("frame return");
        }
        _pilot.Release();
        Hud.BubblePaused = previousPause;
        EasyRequire(frames >= 71 && frames <= 73 && !_boss.BossFrameReturning,
            $"The frame return did not take 1.2 seconds: stage={stage} frames={frames}.");
        PresenceFrameState(true, stage + " complete");
        EasyRequire(_player.GlobalPosition.X <= Field.BossFrameLeft - Player.HitRadius + .01f,
            "The returning frame left the player behind its final boundary.");
        GD.Print($"[BOSS-PRESENCE] FRAME_RETURN stage={stage} frames={frames} maximum_player_step={maximumStep} right_input=true bubble_paused={pausedReturn} teleport=false phase_clocks_frozen_in_actual_boss_physics=true");
        if (deferred != null)
        {
            deferred.Tick(1d / 60);
            for (int frame = 0; frame < 90 && _boss.BossFrameEntering; frame++)
            { deferred.Tick(1d / 60); await EasyPhysicsFrames(1); }
            EasyRequire(!deferred.Finished && deferred.SpellName.Length > 0 && _boss.BossInArena,
                "A deferred chasing technique did not start after the frame return completed.");
            PresenceFrameState(false, "deferred chase start");
            Vector2 beforeCancel = _player.GlobalPosition;
            deferred.Cancel();
            await PresenceFrameReturn("deferred chase cancellation", beforeCancel);
        }
    }

    private async Task PresenceChaseCompletion(BossCharacterTechnique attack, int phase)
    {
        Write(_player, "_invincible", true);
        Write(_player, "_invincibleTimer", 99999f);
        bool ended = false, completed = false;
        Vector2 playerBeforeFinish = _player.GlobalPosition;
        attack.Ended += value => { ended = true; completed = value.Completed; };
        for (int frame = 0; frame < 120 * 60 && !ended; frame++)
        {
            playerBeforeFinish = _player.GlobalPosition;
            attack.Tick(1d / 60);
            if (!ended)
            {
                PresenceFrameState(false, "chase natural completion");
                EasyRequire(_boss.BossInArena, "The chasing body returned before its attack completed.");
            }
            await EasyPhysicsFrames(1);
            if (!ended) PresenceFaceClearance("chase natural completion");
        }
        EasyRequire(ended && completed, "The chasing retry did not complete naturally.");
        await PresenceFrameReturn("chase natural completion", playerBeforeFinish);
        GD.Print($"[BOSS-PRESENCE] CHASE_NATURAL phase={phase} completed=true right_strip_access=true invincible=true");
    }

    private async Task PresencePlayerShot(int phase)
    {
        _player.GlobalPosition = new Vector2(Field.Right - 48, Field.CenterY);
        await EasyPhysicsFrames(3);
        var panels = _boss.GetChildren().OfType<Panel>().ToArray();
        EasyRequire(panels.Length > 0, "The boss frame has no unfolders to target.");
        foreach (var panel in panels) panel.SetPhysicsProcess(false);
        var target = panels.OrderBy(panel => panel.GlobalPosition.X).First();
        EasyRequire(Field.BossFrameRect.HasPoint(target.GlobalPosition), "An unfolder is outside the boss frame.");
        int originalInk = target.Ink;
        target.Ink = Math.Max(4, originalInk);
        int ink = target.Ink;
        target.Invulnerable = false;
        PresenceLockTarget(expectUnfolder: true);
        Vector2 origin = new(Field.Right - 20, target.GlobalPosition.Y);
        var shot = _pool.Spawn(origin, origin.DirectionTo(target.GlobalPosition) * 100, false, damage: 1);
        bool enteredFrame = false;
        for (int frame = 0; frame < 120 && shot.Active; frame++)
        {
            await EasyPhysicsFrames(1);
            enteredFrame |= shot.GlobalPosition.X > Field.Right;
            if (shot.Active && enteredFrame)
                EasyRequire(PresenceDepth(shot) > PresenceDepth(_boss.GetNode<CanvasItem>("BossBattleFrame")),
                    "A player projectile disappears behind the boss frame.");
        }
        EasyRequire(enteredFrame && !shot.Active && target.Ink < ink,
            $"A normal player projectile did not cross into the boss frame and damage an unfolder: origin={origin} target={target.GlobalPosition} shot={shot.GlobalPosition} active={shot.Active} ink={ink}->{target.Ink} entered={enteredFrame} target_layer={target.CollisionLayer} target_mask={target.CollisionMask} invulnerable={target.Invulnerable} rally={_boss.RallyGuarded} practice={_boss.TechniquePractice} paused={Hud.BubblePaused}.");
        int panelDamage = ink - target.Ink;
        target.Ink = originalInk;
        target.Invulnerable = true;
        await EasyPhysicsFrames(3);

        int savedHp = Read<int>(_boss, "_hp");
        Write(_boss, "_hp", _boss.BarHp * _boss.TotalBars);
        Call(_boss, "EnterExposed");
        await EasyPhysicsFrames(4);
        EasyRequire(_boss.GaugeBodyExposed && !_boss.BodyContactDamaging,
            "Exposure does not preserve harmless boss contact.");
        PresenceLockTarget(expectUnfolder: false);
        int hp = Read<int>(_boss, "_hp"), hits = 0;
        float hitX = 0;
        void OnBossHit(Area2D area) { if (area is Bullet { IsEnemy: false }) hits++; }
        _boss.AreaEntered += OnBossHit;
        Vector2 bodyCenter = PresenceBodyCenter(_boss);
        origin = new Vector2(Field.Right - 20, bodyCenter.Y);
        shot = _pool.Spawn(origin, origin.DirectionTo(bodyCenter) * 100, false, damage: 1);
        enteredFrame = false;
        for (int frame = 0; frame < 120 && shot.Active; frame++)
        {
            await EasyPhysicsFrames(1);
            hitX = shot.GlobalPosition.X;
            enteredFrame |= hitX > Field.Right;
        }
        _boss.AreaEntered -= OnBossHit;
        EasyRequire(enteredFrame && hits > 0 && !shot.Active && Read<int>(_boss, "_hp") < hp,
            "A normal projectile did not cross into the frame and damage the exposed boss through native AreaEntered.");
        int damage = hp - Read<int>(_boss, "_hp");
        Call(_boss, "EnterReclose");
        Call(_boss, "EnterShielded");
        Write(_boss, "_hp", savedHp);
        _hud.HideBubble();
        Hud.BubblePaused = false;
        foreach (var panel in panels) panel.SetPhysicsProcess(true);
        await EasyPhysicsFrames(4);
        GD.Print($"[BOSS-PRESENCE] SHOT phase={phase} unfolder_damage={panelDamage} exposed_hits={hits} hp_damage={damage} hit_x={hitX} crossed_frame=true shift_lock_and_release=true projectile_spawn_fixture=true");
    }

    private async Task PresenceEnemyBoundary(int phase)
    {
        _player.GlobalPosition = EasyStarts[0];
        var bullet = _pool.Spawn(new Vector2(Field.Right - 12, Field.Bottom - 24), Vector2.Right * 100, true);
        var bossFrame = _boss.GetNode<CanvasItem>("BossBattleFrame");
        bool masked = false;
        for (int frame = 0; frame < 120 && bullet.Active; frame++)
        {
            await EasyPhysicsFrames(1);
            if (!bullet.Active) break;
            EasyRequire(PresenceDepth(bullet) < PresenceDepth(bossFrame) && bossFrame.IsVisibleInTree(),
                "An enemy projectile draws over the opaque boss frame.");
            masked |= bullet.GlobalPosition.X > Field.Right + bullet.CollisionRadius;
            EasyRequire(_player.GlobalPosition.X <= Field.Right - Player.HitRadius,
                "The player entered the reserved boss frame during projectile masking.");
        }
        EasyRequire(masked && !bullet.Active,
            "An outward enemy projectile disappeared before clearing the arena or failed to retire beyond its margin.");
        GD.Print($"[BOSS-PRESENCE] ENEMY_BOUNDARY phase={phase} right={Field.Right} masked_in_boss_frame=true natural_retirement=true");
    }

    private async Task PresenceFlowerReentry(int phase)
    {
        if (_boss is not BossKoharu || phase != 0 || _game.Difficulty != GameManager.Diff.Hard) return;
        _player.GlobalPosition = new Vector2(118, 78);
        Write(_player, "_invincible", true);
        Write(_player, "_invincibleTimer", 99999f);
        var attack = (KoharuBossTechnique)BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Normal, 2);
        attack.SetPhysicsProcess(false);
        var path = Read<System.Collections.Generic.List<KoharuBossTechnique.KoharuEasyPath>>(attack, "_easyPaths")
            .Single(path => path.Wave == 0 && path.Strand == 10 && path.Pulse == 0);
        bool outside = false, returned = false;
        float maximumX = Field.Left;
        for (int frame = 0; frame < 15 * 60 && !attack.Finished; frame++)
        {
            attack.Tick(1d / 60);
            await EasyPhysicsFrames(1);
            if (!path.Fired) continue;
            EasyRequire(path.Shot is { Active: true } && path.Shot.ActivationId == path.Activation,
                "The flower curve was culled before its announced return from the boss frame.");
            var shot = path.Shot!;
            maximumX = Math.Max(maximumX, shot.GlobalPosition.X);
            outside |= shot.GlobalPosition.X > Field.Right + 6;
            EasyRequire(PresenceDepth(shot) < PresenceDepth(_boss.GetNode<CanvasItem>("BossBattleFrame")),
                "The returning flower draws over the boss frame.");
            if (outside && shot.GlobalPosition.X < Field.Right - 1 && Field.Rect.HasPoint(shot.GlobalPosition))
            {
                returned = true;
                break;
            }
        }
        attack.Cancel();
        await EasyPhysicsFrames(3);
        EasyRequire(outside && returned && !_boss.BossInArena && Field.BossFrameRect.HasPoint(_boss.GlobalPosition),
            "The flower regression did not exercise a living projectile leaving and re-entering the arena.");
        GD.Print($"[BOSS-PRESENCE] FLOWER_REENTRY phase={phase} maximum_x={maximumX} right={Field.Right} same_activation=true invincible=true fixture_start=true");
    }

    private async Task PresenceDamageComparators(int phase)
    {
        _player.GlobalPosition = new Vector2(Field.Left + 24, Field.Bottom - 32);
        await EasyPhysicsFrames(3);
        PresenceDamageEnabled();
        int lives = _player.Lives;
        var bullet = _pool.Spawn(_player.GlobalPosition + Vector2.Right * 24, Vector2.Left * 90, true);
        int entries = 0;
        void OnBulletEntered(Area2D area) { if (area == bullet) entries++; }
        _player.AreaEntered += OnBulletEntered;
        for (int frame = 0; frame < 120 && _player.Lives == lives && bullet.Active; frame++)
            await EasyPhysicsFrames(1);
        _player.AreaEntered -= OnBulletEntered;
        if (_player.Lives != lives - 1)
        {
            var hitShape = _player.GetNode<CollisionShape2D>("HitShape");
            GD.Print($"[BOSS-PRESENCE] NATIVE player_monitoring={_player.Monitoring} player_monitorable={_player.Monitorable} player_mask={_player.CollisionMask} player_native={PhysicsServer2D.AreaGetTransform(_player.GetRid())} hit_shape={hitShape.GlobalTransform} hit_disabled={hitShape.Disabled} hit_size={hitShape.Shape.GetRect()} bullet_monitoring={bullet.Monitoring} bullet_monitorable={bullet.Monitorable} bullet_native={PhysicsServer2D.AreaGetTransform(bullet.GetRid())} player_space={PhysicsServer2D.AreaGetSpace(_player.GetRid())} bullet_space={PhysicsServer2D.AreaGetSpace(bullet.GetRid())}");
        }
        EasyRequire(_player.Lives == lives - 1,
            $"Enemy bullets became harmless with boss bodies: lives={lives}->{_player.Lives} player={_player.GlobalPosition} bullet={bullet.GlobalPosition} active={bullet.Active} entries={entries} overlap={_player.GetOverlappingAreas().Contains(bullet)} layer={bullet.CollisionLayer} mask={bullet.CollisionMask} shapes={string.Join(",", bullet.GetShapeOwners().Select(owner => $"{owner}:{bullet.IsShapeOwnerDisabled((uint)owner)}:{bullet.ShapeOwnerGetShapeCount((uint)owner)}"))} invincible={Read<bool>(_player, "_invincible")} dodge={Read<float>(_player, "_dodgeInv")} veil={Read<float>(_player, "_veilT")} paused={Hud.BubblePaused}.");
        _pool.Despawn(bullet);
        await EasyPhysicsFrames(3);

        var enemy = new Enemy { Position = new Vector2(Field.Left + 48, Field.CenterY) };
        _easyRoot!.GetNode<Node2D>("World").AddChild(enemy);
        enemy.SetProcess(false);
        enemy.SetPhysicsProcess(false);
        _player.GlobalPosition = new Vector2(Field.Left + 8, Field.Bottom - 8);
        await EasyPhysicsFrames(3);
        PresenceDamageEnabled();
        lives = _player.Lives;
        _player.GlobalPosition = PresenceBodyCenter(enemy);
        await EasyPhysicsFrames(4);
        EasyRequire(enemy.BodyContactDamaging && _player.GetOverlappingAreas().Contains(enemy) && _player.Lives == lives - 1,
            "Ordinary enemy entry contact stopped damaging the player.");
        PresenceDamageEnabled();
        lives = _player.Lives;
        Pad.ReleaseMouseFollow();
        _player._PhysicsProcess(1d / 60);
        EasyRequire(_player.Lives == lives - 1, "Persistent ordinary enemy contact stopped damaging the player.");
        enemy.QueueFree();
        await EasyPhysicsFrames(4);
        PresenceDamageEnabled();
        GD.Print($"[BOSS-PRESENCE] DAMAGE phase={phase} enemy_bullet=true ordinary_enemy_entry=true ordinary_enemy_persistent=true invincible=false");
    }

    private async Task PresenceNaturalCompletion(int phase)
    {
        int variant = _boss switch { BossAkari => 1, BossKoharu => 2, BossRei => 3, _ => phase };
        _player.GlobalPosition = EasyStarts[0];
        Write(_player, "_invincible", true);
        Write(_player, "_invincibleTimer", 99999f);
        var attack = BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Area, variant);
        _easyAttack = attack;
        attack.SetPhysicsProcess(false);
        bool ended = false, completed = false, hazardDamaged = false;
        attack.Ended += value => { ended = true; completed = value.Completed; };
        for (int frame = 0; frame < 180 * 60 && !ended; frame++)
        {
            attack.Tick(1d / 60);
            if (!attack.UsesArenaBody)
            {
                EasyRequire(!_boss.BossInArena && Field.BossFrameRect.HasPoint(_boss.GlobalPosition),
                    "A framed Area technique moved the boss out of its frame.");
                EasyRequire(!attack.CoversPoint(new Vector2(Field.BossFrameCenterX, Field.CenterY)),
                    "A live technique covers the protected boss frame.");
            }
            if (!ended && !hazardDamaged && attack.IsStriking)
            {
                Vector2? hit = null;
                for (float y = Field.Top + 3; y < Field.Bottom && hit == null; y += 3)
                for (float x = Field.Left + 3; x < Field.Right; x += 3)
                    if (attack.CoversPoint(new Vector2(x, y))) { hit = new Vector2(x, y); break; }
                if (hit is { } point)
                {
                    _player.GlobalPosition = point;
                    PresenceDamageEnabled();
                    int lives = _player.Lives;
                    attack.Tick(1d / 60);
                    hazardDamaged = _player.Lives == lives - 1;
                    Write(_player, "_invincible", true);
                    Write(_player, "_invincibleTimer", 99999f);
                }
            }
            await EasyPhysicsFrames(1);
        }
        EasyRequire(ended && completed && hazardDamaged,
            $"Live hazard damage or natural completion was not verified: phase={phase} ended={ended} completed={completed} hazard_damage={hazardDamaged}.");
        await SettleEasyFrame();
        EasyRequire(!_boss.BossInArena && Field.BossFrameRect.HasPoint(_boss.GlobalPosition),
            "Natural completion did not return the boss to its frame.");
        _easyAttack = null;
        _pool.DespawnAll();
        await EasyPhysicsFrames(4);
        GD.Print($"[BOSS-PRESENCE] NATURAL phase={phase} kind=Area variant={variant} completed=true explicit_hazard_damage=true fixture_positioning=true completion_invincible=true");
    }

    private async Task RunBossPresenceQa(int phase)
    {
        _presencePortraitChecks = _presenceFadeChecks = 0;
        _running = false;
        _pilot.Release();
        _pool.DespawnAll();
        Write(_player, "_fireCooldown", 99999f);
        EasyRequire(Math.Abs(Field.Right - Field.BossFrameLeft) < .01f
            && Math.Abs(Field.WorldRight - Field.BossFrameLeft - Field.BossFrameWidth) < .01f,
            "The active boss frame does not reserve the right HUD-width strip.");
        EasyRequire(!_boss.BossInArena && Field.BossFrameRect.HasPoint(_boss.GlobalPosition),
            "The idle boss is not inside its frame.");
        PresenceFrameState(true, "idle");
        PresenceRenderOrder();
        PresenceFacingQa(phase);
        bool holdingFacing = PresenceHoldingFacingQa(phase);
        PresencePortraitFade(phase);
        PresenceVerticalPatrol(phase);
        PresenceFaceOrbit(phase);
        if (OS.GetCmdlineUserArgs().Contains("--boss-portrait-qa")
            && !OS.GetCmdlineUserArgs().Contains("--boss-presence-qa"))
        {
            EasyRequire(_presencePortraitChecks > 0 && _presenceFadeChecks > 0,
                "Portrait-only QA did not exercise native scale and outgoing-frame clipping.");
            _easyCases.Add(new { audit = "boss_portrait", character = _scene, difficulty = _game.Difficulty.ToString(), phase,
                passed = true, portrait_native_scale_preserved = true, portrait_geometry_checks = _presencePortraitChecks,
                portrait_fade_clipping_checks = _presenceFadeChecks, face_orbit_samples = 96,
                portrait_semantic_facing = true, texture_swap_and_motion_frame_facing = true,
                chasing_holding_facing = holdingFacing,
                fixture_positioning = true, human_comprehension_test = false });
            GD.Print($"[BOSS-PORTRAIT] COMPLETE boss={_scene} difficulty={_game.Difficulty} phase={phase} geometry_checks={_presencePortraitChecks} fade_checks={_presenceFadeChecks} passed=true");
            return;
        }
        await PresencePlayerBoundary("before technique");
        await PresencePlayerShot(phase);
        await PresenceEnemyBoundary(phase);
        await PresenceFlowerReentry(phase);
        var slots = BossAttackCycle.EasySlots(_boss, phase).Distinct().ToList();
        if (phase == 1 && _boss is BossRei or BossKoharu) slots.Add(new(BossTechniqueKind.Finale));
        foreach (var slot in slots)
        {
            _player.GlobalPosition = EasyStarts[0];
            if (_boss is BossRei && slot.Kind == BossTechniqueKind.Normal && slot.Variant is 3 or 4)
                PrepareSourceCutPanels();
            var attack = BossCharacterTechnique.Begin(_boss, slot.Kind, slot.Variant);
            _easyAttack = attack;
            attack.SetPhysicsProcess(false);
            attack.SetProcess(false);
            bool chasing = _boss switch {
                BossAkari or BossRei => slot.Kind == BossTechniqueKind.Normal && slot.Variant is 1 or 5 || slot.Kind == BossTechniqueKind.Signature,
                BossKoharu => slot.Kind == BossTechniqueKind.Normal && slot.Variant == 1
                    || slot.Kind == BossTechniqueKind.Crossfire && slot.Variant == 0
                    || slot.Kind == BossTechniqueKind.Extra && phase == 1 && slot.Variant == 0,
                _ => slot.Kind == BossTechniqueKind.Area
                    || slot.Kind == BossTechniqueKind.Extra && slot.Variant == 1,
            };
            EasyRequire(attack.UsesArenaBody == chasing && _boss.BossInArena == chasing,
                "The attack does not match its explicit chasing exception.");
            await EasyPhysicsFrames(2);
            PresenceFrameState(!chasing, "technique start");
            if (chasing) await PresencePlayerBoundary("chasing technique", frameActive: false);
            EasyRequire(PresenceDepth(attack) < PresenceDepth(_boss.GetNode<CanvasItem>("BossBattleFrame")),
                "The technique drawing layer can cover the boss frame.");
            Write(_player, "_invincible", true);
            Write(_player, "_invincibleTimer", 99999f);
            Vector2 initialBossPosition = _boss.GlobalPosition;
            bool enteredArena = false, observedLive = false;
            for (int frame = 0; frame < 12 * 60 && !attack.Finished; frame++)
            {
                attack.Tick(1d / 60);
                await EasyPhysicsFrames(1);
                if (attack.Finished) break;
                enteredArena |= Field.Rect.HasPoint(_boss.GlobalPosition)
                    && _boss.GlobalPosition.DistanceTo(initialBossPosition) > 4;
                EasyRequire(attack.Finished || _boss.BossInArena == chasing,
                    "The boss changed its frame/arena role in the middle of a technique.");
                PresenceFrameState(!chasing, "technique active");
                PresenceFaceClearance("technique active");
                if (!chasing) EasyRequire(Field.BossFrameRect.HasPoint(_boss.GlobalPosition),
                    "A non-chasing technique moved the boss into the playable field.");
                if (!chasing)
                    for (float y = Field.Top + 4; y < Field.Bottom; y += 24)
                        EasyRequire(!attack.CoversPoint(new Vector2(Field.BossFrameLeft + 4, y)),
                            "A technique damages the reserved boss frame.");
                observedLive |= attack.IsStriking || attack.OwnedShots.Any(bullet => !bullet.LeadIn && bullet.CollisionLayer != 0);
                EasyRequire(attack.OwnedShots.All(bullet => PresenceDepth(bullet)
                    < PresenceDepth(_boss.GetNode<CanvasItem>("BossBattleFrame"))),
                    "An enemy projectile drawing layer covers the boss frame.");
                if (observedLive && (!chasing || enteredArena)) break;
            }
            EasyRequire(!chasing || enteredArena, "A chasing exception never entered the playable field.");
            EasyRequire((_boss.CollisionLayer & 4) != 0 && _boss.Monitorable,
                "A technique removed the boss from player projectile collision.");
            Vector2 beforeCancel = _player.GlobalPosition;
            attack.Cancel();
            _easyAttack = null;
            if (chasing) await PresenceFrameReturn("chase cancellation", beforeCancel, slot);
            await EasyPhysicsFrames(4);
            EasyRequire(!_boss.BossInArena && Field.BossFrameRect.HasPoint(_boss.GlobalPosition),
                "Cancelled technique did not restore the boss frame position.");
            PresenceFrameState(true, "after cancellation");
            var retry = BossCharacterTechnique.Begin(_boss, slot.Kind, slot.Variant);
            retry.SetPhysicsProcess(false);
            await EasyPhysicsFrames(2);
            PresenceFrameState(!chasing, "retry start");
            if (chasing)
            {
                await PresencePlayerBoundary("chasing retry", frameActive: false);
                await PresenceChaseCompletion(retry, phase);
            }
            else retry.Cancel();
            await EasyPhysicsFrames(3);
            EasyRequire(!_boss.BossInArena && Field.BossFrameRect.HasPoint(_boss.GlobalPosition),
                "Cancelling a retry did not restore the boss frame position.");
            PresenceFrameState(true, "after retry");
            GD.Print($"[BOSS-PRESENCE] SLOT phase={phase} kind={slot.Kind} variant={slot.Variant} chasing={chasing} entered_arena={enteredArena} observed_live={observedLive} cancel=true retry=true retry_completed={chasing} invincible=true");
        }
        await PresencePlayerBoundary("after cancellation");
        await PresenceNaturalCompletion(phase);
        await PresenceDamageComparators(phase);
        _player.GlobalPosition = EasyStarts[0];
        EasyRequire(_presencePortraitChecks > 0 && _presenceFadeChecks > 0,
            "Portrait scale and outgoing-frame clipping were not exercised.");
        _easyCases.Add(new { audit = "boss_presence", character = _scene, difficulty = _game.Difficulty.ToString(), phase,
            slots = slots.Count, passed = true, player_stopped_at_arena_edge = true, masked_in_boss_frame = true,
            chase_right_strip_access = true, chase_frame_return_no_teleport = true,
            portrait_native_scale_preserved = true, portrait_geometry_checks = _presencePortraitChecks,
            portrait_fade_clipping_checks = _presenceFadeChecks,
            portrait_semantic_facing = true, texture_swap_and_motion_frame_facing = true,
            chasing_holding_facing = holdingFacing,
            player_shots_cross_frame = true, unfolders_take_shot_damage = true, exposed_body_takes_shot_damage = true, live_hazards_damage = true,
            framed_boss_and_unfolder_lock_input = true,
            enemy_bullets_damage = true, ordinary_enemy_contact_damage = true, cancellation_retry = true,
            natural_completion = true, fixture_positioning = true, human_comprehension_test = false });
        GD.Print($"[BOSS-PRESENCE] COMPLETE boss={_scene} difficulty={_game.Difficulty} phase={phase} slots={slots.Count} passed=true");
    }
}
