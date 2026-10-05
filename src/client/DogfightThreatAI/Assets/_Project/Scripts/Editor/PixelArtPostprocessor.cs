using UnityEditor;
using UnityEngine;

namespace Dogfight.EditorTools
{
    /// <summary>
    /// 像素素材导入后处理：约定目录下的贴图自动设成像素风格。
    ///
    /// 为什么必须自动化：我们的素材策略是"先用第三方占位、之后再换自己的"。
    /// 如果靠人手在 Inspector 里改 PPU / Filter / Compression，
    /// 换一套素材就会漏几张，表现为"个别飞机糊了"这种很难查的问题。
    /// 这里把基线固化下来 —— 入口统一，谁导入都一样。
    ///
    /// 基线：PPU = 32，Point 过滤，不压缩，不生成 mipmap。
    /// 只作用于下面 WatchedFolders 里的资源，不动 Packages 与 Unity 内置资源。
    /// </summary>
    public sealed class PixelArtPostprocessor : AssetPostprocessor
    {
        /// <summary>全局像素密度基线。与 docs/02-design/美术规范.md 必须一致。</summary>
        public const int PixelsPerUnit = 32;

        static readonly string[] WatchedFolders =
        {
            "Assets/_Project/Art/",
            "Assets/ThirdParty/",
        };

        void OnPreprocessTexture()
        {
            if (!IsWatched(assetPath)) return;

            var importer = assetImporter as TextureImporter;
            if (importer == null) return;

            // 只处理精灵用途的图；法线贴图、UI 图集等留给各自的后处理器
            TextureImporterType desiredType = TextureImporterType.Sprite;

            bool changed = false;

            if (importer.textureType != desiredType)
            {
                importer.textureType = desiredType;
                changed = true;
            }

            if (importer.spritePixelsPerUnit != PixelsPerUnit)
            {
                importer.spritePixelsPerUnit = PixelsPerUnit;
                changed = true;
            }

            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                changed = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }

            if (importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                changed = true;
            }

            if (importer.alphaIsTransparency == false)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            if (!importer.sRGBTexture)
            {
                importer.sRGBTexture = true;
                changed = true;
            }

            // 压缩设置：走平台默认，但像素画一律不压缩，否则会出现色块
            TextureImporterPlatformSettings platform = importer.GetDefaultPlatformTextureSettings();
            if (platform.textureCompression != TextureImporterCompression.Uncompressed)
            {
                platform.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SetPlatformTextureSettings(platform);
                changed = true;
            }

            if (changed)
            {
                Debug.Log("[像素后处理] " + assetPath + " → PPU " + PixelsPerUnit + " / Point / 不压缩");
            }
        }

        static bool IsWatched(string path)
        {
            for (int i = 0; i < WatchedFolders.Length; i++)
            {
                if (path.StartsWith(WatchedFolders[i], System.StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
