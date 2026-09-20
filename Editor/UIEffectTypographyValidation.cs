#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Lxy.UIEffectGenerator.Editor
{
    internal static partial class UIEffectFidelityValidation
    {
        private static void ValidateTypography(string folder, List<string> checks)
        {
            TMP_FontAsset original = TMP_Settings.defaultFontAsset;
            Require(UIEffectTypography.IsUsable(original), "project default TMP font is usable", checks);
            TMP_FontAsset schemaFont = Object.Instantiate(original);
            string fontPath = folder + "/PortableFont.asset";
            AssetDatabase.CreateAsset(schemaFont, fontPath);
            var compatible = new Material(original.material);
            string materialPath = folder + "/PortableFont.mat";
            AssetDatabase.CreateAsset(compatible, materialPath);
            var wrongTexture = new Texture2D(4, 4);
            AssetDatabase.CreateAsset(wrongTexture, folder + "/WrongAtlas.asset");
            var incompatible = new Material(original.material) { mainTexture = wrongTexture };
            string wrongMaterialPath = folder + "/WrongAtlas.mat";
            AssetDatabase.CreateAsset(incompatible, wrongMaterialPath);
            string missingFont = folder + "/PortableFontTypo.asset";

            var schema = new UIEffectSchema { name = "Typography", designWidth = 400, designHeight = 200,
                children = new List<UIEffectNode>
                {
                    new UIEffectNode { name = "Correct", type = "Text", text = "A", width = 200,
                        height = 60, font = fontPath, fontMaterial = materialPath },
                    new UIEffectNode { name = "Misspelled", type = "Text", text = "B", y = 70,
                        width = 200, height = 60, font = missingFont },
                } };
            var warnings = UIEffectTypography.Prepare(schema, null);
            Require(schema.children[0].font == fontPath && schema.children[0].fontMaterial == materialPath,
                "valid explicit font and compatible material are preserved", checks);
            Require(schema.children[1].font == fontPath && warnings.Count == 1,
                "missing font uses an existing schema font instead of an unrelated default", checks);

            schema.children[1].font = missingFont;
            UIEffectTypography.Prepare(schema, original);
            Require(schema.children[1].font == AssetDatabase.GetAssetPath(original) &&
                schema.children[0].font == fontPath, "selected default repairs missing overrides without replacing valid fonts", checks);
            schema.children[1].font = string.Empty;
            UIEffectTypography.Prepare(schema, schemaFont);
            Require(schema.children[1].font == fontPath, "selected project font is used for unspecified typography", checks);

            schema.children[1].fontMaterial = folder + "/MissingMaterial.mat";
            warnings = UIEffectTypography.Prepare(schema, null);
            Require(warnings.Count == 1 && schema.children[1].fontMaterial == string.Empty,
                "missing font material falls back to the font material", checks);
            schema.children[1].fontMaterial = wrongMaterialPath;
            warnings = UIEffectTypography.Prepare(schema, null);
            Require(warnings.Count == 1 && schema.children[1].fontMaterial == string.Empty,
                "mismatched font atlas cannot be assigned as a text material", checks);
            Material noTexture = new Material(original.material) { mainTexture = null };
            try { Require(!UIEffectTypography.IsCompatible(noTexture, schemaFont),
                "null material texture is never accepted as a compatible atlas", checks); }
            finally { Object.DestroyImmediate(noTexture); }

            schema.children[1].font = missingFont;
            schema.children[1].fontMaterial = wrongMaterialPath;
            string input = UIEffectSchemaUtility.ToCompactJson(schema);
            var repairs = new List<string>();
            UIEffectSchema ai = UIEffectSchemaUtility.Parse(input);
            UIEffectTypography.RemoveUnevidencedAiFonts(ai.children, repairs);
            Require(repairs.Count == 2 && ai.children.All(node => node.font == "" && node.fontMaterial == "") &&
                UIEffectSchemaUtility.Parse(input).children[0].font == fontPath,
                "AI font paths are removed while explicit saved Schema fields remain supported", checks);

            var result = UIEffectPrefabBuilder.Generate(input,
                new UIEffectPrefabGenerationOptions { panelId = "UITypographyValidation", prefabFolder = folder,
                    resourceMatchMode = UIEffectResourceMatchMode.ColorBlocks }, false);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
            TMP_Text[] texts = prefab.GetComponentsInChildren<TMP_Text>(true);
            Require(texts.Length == 2 && texts.All(text => text.font == schemaFont) && result.Warnings.Count == 2,
                "Prefab generation succeeds with missing font and incompatible material, reporting both repairs", checks);
            Require(UIEffectSchemaUtility.ToCompactJson(schema) == input,
                "font resolution does not rewrite the caller's source Schema", checks);
            Require(texts[1].rectTransform.sizeDelta == new Vector2(200, 60) && texts[1].fontSize == 32,
                "font recovery preserves measured text geometry and size", checks);
        }
    }
}
#endif
