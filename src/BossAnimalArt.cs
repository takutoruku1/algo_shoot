using Godot;

public static class BossAnimalArt
{
    private const float DisplayScale = .68f;
    public static string Path(string id, string pose)
        => $"res://char/v3/animal_forms/{id}_animal_{pose}_v1.png";

    public static bool Contains(Texture2D texture)
        => texture.ResourcePath.Contains("/animal_forms/") || texture.ResourcePath.Contains("_animal_breath_");

    public static (float Scale, Vector2 Offset) Frame(Texture2D texture)
    {
        // Register the torso across poses; tails, ribbons and antlers must not change the body scale.
        var (canvasHeight, pixelScale, core) = texture.ResourcePath.GetFile() switch
        {
            "akari_animal_idle_v1.png" => (662f, 0.1514196f, new Vector2(280, 365)),
            "akari_animal_attack_v1.png" => (620f, 0.1514196f, new Vector2(290, 360)),
            "akari_animal_down_v1.png" => (634f, 0.1514196f, new Vector2(255, 350)),
            "koharu_animal_idle_v1.png" => (724f, 0.1436782f, new Vector2(240, 445)),
            "koharu_animal_attack_v1.png" => (630f, 0.1436782f, new Vector2(400, 430)),
            "koharu_animal_down_v1.png" => (609f, 0.1436782f, new Vector2(350, 320)),
            "rei_animal_idle_v1.png" => (602f, 0.1745201f, new Vector2(300, 270)),
            "rei_animal_attack_v1.png" => (640f, 0.1745201f, new Vector2(300, 340)),
            "rei_animal_down_v1.png" => (630f, 0.1745201f, new Vector2(210, 240)),
            "akari_animal_breath_0_v1.png" => (395f, 0.2400000f, new Vector2(185, 245)),
            "akari_animal_breath_1_v1.png" => (395f, 0.2400000f, new Vector2(180, 245)),
            "akari_animal_breath_2_v1.png" => (395f, 0.2400000f, new Vector2(182, 245)),
            "koharu_animal_breath_0_v1.png" => (423f, 0.2079208f, new Vector2(257, 288)),
            "koharu_animal_breath_1_v1.png" => (423f, 0.2079208f, new Vector2(226, 288)),
            "koharu_animal_breath_2_v1.png" => (423f, 0.2079208f, new Vector2(220, 288)),
            "rei_animal_breath_0_v1.png" => (419f, 0.2575000f, new Vector2(160, 195)),
            "rei_animal_breath_1_v1.png" => (421f, 0.2575000f, new Vector2(157, 197)),
            "rei_animal_breath_2_v1.png" => (424f, 0.2575000f, new Vector2(158, 200)),
            "akari_animal_move_0_v1.png" => (512f, 0.24f, new Vector2(212, 293)),
            "akari_animal_move_1_v1.png" => (512f, 0.24f, new Vector2(213, 286)),
            "akari_animal_move_2_v1.png" => (512f, 0.24f, new Vector2(227, 306)),
            "akari_animal_move_3_v1.png" => (512f, 0.24f, new Vector2(206, 293)),
            "akari_animal_windup_v1.png" => (512f, 0.24f, new Vector2(246, 281)),
            "akari_animal_recover_v1.png" => (512f, 0.24f, new Vector2(217, 289)),
            "koharu_animal_move_0_v1.png" => (530f, 0.222f, new Vector2(232, 342)),
            "koharu_animal_move_1_v1.png" => (530f, 0.222f, new Vector2(220, 343)),
            "koharu_animal_move_2_v1.png" => (530f, 0.222f, new Vector2(227, 350)),
            "koharu_animal_move_3_v1.png" => (494f, 0.222f, new Vector2(233, 303)),
            "koharu_animal_windup_v1.png" => (494f, 0.222f, new Vector2(269, 332)),
            "koharu_animal_recover_v1.png" => (494f, 0.222f, new Vector2(222, 310)),
            "rei_animal_move_0_v1.png" => (530f, 0.23f, new Vector2(227, 285)),
            "rei_animal_move_1_v1.png" => (530f, 0.23f, new Vector2(198, 310)),
            "rei_animal_move_2_v1.png" => (530f, 0.23f, new Vector2(195, 327)),
            "rei_animal_move_3_v1.png" => (494f, 0.23f, new Vector2(235, 256)),
            "rei_animal_windup_v1.png" => (494f, 0.23f, new Vector2(228, 271)),
            "rei_animal_recover_v1.png" => (494f, 0.23f, new Vector2(219, 264)),
            _ => throw new System.ArgumentOutOfRangeException(nameof(texture), texture.ResourcePath, "Unknown animal art"),
        };
        return (canvasHeight * pixelScale * DisplayScale / 56f, texture.GetSize() / 2f - core * texture.GetHeight() / canvasHeight);
    }
}
