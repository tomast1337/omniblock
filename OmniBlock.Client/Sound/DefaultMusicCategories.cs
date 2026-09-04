namespace OmniBlock.Client.Sound;

public static class DefaultMusicCategories
{
    public static readonly ResourceLocation Game = "game";
    public static readonly ResourceLocation Menu = "menu";

    public static void Register(SoundManager soundManager)
    {
        soundManager.RegisterMusicCategory(Game, 12000, 24000);
        soundManager.RegisterMusicCategory(Menu, 20, 600);
    }
}
