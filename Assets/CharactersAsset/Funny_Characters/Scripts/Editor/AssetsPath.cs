namespace CharacterCustomization
{
    public static class AssetsPath
    {
        public const string PackTitle = "Funny_Characters";
        public const string BasicCharacterName = "Basic_Character";

        private static string _root = "Assets/ithappy/" + PackTitle + "/";

        public static string MainMaterial => _root + "Materials/Material.mat";
        public static string GlassMaterial => _root + "Materials/Glass.mat";
        public static string EmissionMaterial => _root + "Materials/Emission.mat";
        public static string Parts => _root + "Meshes";
        public static string FullBody => _root + "Meshes/Full_Body";
        public static string Fbx => _root + "Meshes/" + BasicCharacterName + ".fbx";
        public static string AnimationController => _root + "Animations/AnimationController.controller";
        public static string SavedCharacters => _root + "Saved_Characters/";

        public static void SetRoot(string root)
        {
            _root = root;
        }
    }
}