using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Luban;
using PromeArchTrial.Game.Character;
using PromeArchTrial.Game.ConfigAdapter;
using UnityEngine;
using UnityEngine.Networking;
using LubanTables = PromeArchTrial.Config.Tables;

namespace PromeArchTrial.Game.Unity.Config
{
    /// <summary>
    /// 聚合一次客户端加载得到的 Luban 原始表、纯 C# 角色配置与客户端表现配置，调用方可把三个生命周期绑定到同一场战斗。
    /// </summary>
    public sealed class CharacterLubanClientConfig
    {
        /// <summary>创建一个已经完成全部 ref 与运行时约束校验的客户端配置包。</summary>
        public CharacterLubanClientConfig(LubanTables tables, CharacterRuntimeConfig runtimeConfig, CharacterLubanPresentationConfig presentationConfig)
        {
            Tables = tables ?? throw new ArgumentNullException(nameof(tables));
            RuntimeConfig = runtimeConfig ?? throw new ArgumentNullException(nameof(runtimeConfig));
            PresentationConfig = presentationConfig ?? throw new ArgumentNullException(nameof(presentationConfig));
        }

        /// <summary>获取已反序列化并完成 ResolveRef 的 Luban 表集合。</summary>
        public LubanTables Tables { get; }

        /// <summary>获取客户端预测与服务器权威模拟共用的纯 C# 配置。</summary>
        public CharacterRuntimeConfig RuntimeConfig { get; }

        /// <summary>获取只在 Unity 客户端存在的 prefab 与 Spine 动画绑定。</summary>
        public CharacterLubanPresentationConfig PresentationConfig { get; }
    }

    /// <summary>
    /// 从 StreamingAssets/PromeArchTrial/Config 加载 Luban cs-bin 数据；桌面平台支持同步加载，压缩包或 URL 型 StreamingAssets 使用异步 UnityWebRequest。
    /// </summary>
    public static class CharacterLubanClientConfigLoader
    {
        /// <summary>保持与生成 Tables 构造函数一致的客户端二进制表名；新增 client 分组表时必须同步更新该列表。</summary>
        private static readonly string[] RequiredBinaryTableNames = { "gameplay_tbbattlerule", "gameplay_tbcharacterproperty", "gameplay_tblocomotion", "gameplay_tbdodge", "gameplay_tbaction", "gameplay_tbactionset", "gameplay_tbcharacter", "gameplay_tbanimationclip", "gameplay_tbcharacterpresentation" };

        /// <summary>同步读取本地文件型 StreamingAssets，并直接用 Character ID 选择角色配置。</summary>
        public static CharacterLubanClientConfig LoadFromStreamingAssets(int characterId)
        {
            string streamingAssetsRoot = Application.streamingAssetsPath;
            if (RequiresUnityWebRequest(streamingAssetsRoot)) throw new PlatformNotSupportedException("This platform exposes StreamingAssets through a URL or archive. Use LoadFromStreamingAssetsAsync instead.");
            string configDirectory = Path.Combine(streamingAssetsRoot, "PromeArchTrial", "Config");
            LubanTables tables = new LubanTables(tableName => new ByteBuf(ReadLocalTableBytes(configDirectory, tableName)));
            return BuildValidatedConfig(tables, characterId);
        }

        /// <summary>跨平台异步读取全部客户端表；URL/归档路径使用 UnityWebRequest，本地路径直接读取文件。</summary>
        public static async Task<CharacterLubanClientConfig> LoadFromStreamingAssetsAsync(int characterId, CancellationToken cancellationToken = default)
        {
            string streamingAssetsRoot = Application.streamingAssetsPath;
            Dictionary<string, byte[]> tableBytes = new Dictionary<string, byte[]>(RequiredBinaryTableNames.Length, StringComparer.Ordinal);
            if (RequiresUnityWebRequest(streamingAssetsRoot))
            {
                foreach (string tableName in RequiredBinaryTableNames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string uri = BuildStreamingAssetUri(streamingAssetsRoot, tableName);
                    tableBytes.Add(tableName, await ReadTableBytesWithUnityWebRequestAsync(uri, cancellationToken));
                }
            }
            else
            {
                string configDirectory = Path.Combine(streamingAssetsRoot, "PromeArchTrial", "Config");
                foreach (string tableName in RequiredBinaryTableNames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    tableBytes.Add(tableName, ReadLocalTableBytes(configDirectory, tableName));
                }
            }
            LubanTables tables = new LubanTables(tableName => new ByteBuf(GetPreloadedTableBytes(tableBytes, tableName)));
            return BuildValidatedConfig(tables, characterId);
        }

        /// <summary>同时编译共享模拟配置和客户端表现绑定，任何无效 ref 都会阻止战斗启动。</summary>
        private static CharacterLubanClientConfig BuildValidatedConfig(LubanTables tables, int characterId)
        {
            CharacterRuntimeConfig runtimeConfig = CharacterLubanConfigAdapter.Compile(tables, characterId);
            CharacterLubanPresentationConfig presentationConfig = CharacterLubanPresentationConfigBuilder.Build(tables, characterId);
            return new CharacterLubanClientConfig(tables, runtimeConfig, presentationConfig);
        }

        /// <summary>从本地配置目录读取一个由 Luban 生成的 .bytes 文件，并在缺失或为空时给出明确错误。</summary>
        private static byte[] ReadLocalTableBytes(string configDirectory, string tableName)
        {
            string filePath = Path.Combine(configDirectory, tableName + ".bytes");
            if (!File.Exists(filePath)) throw new FileNotFoundException($"Luban table binary does not exist: {filePath}. Run Configs/Luban/generate-client.bat first.", filePath);
            byte[] bytes = File.ReadAllBytes(filePath);
            if (bytes.Length == 0) throw new InvalidDataException($"Luban table binary is empty: {filePath}.");
            return bytes;
        }

        /// <summary>从预加载字典中取得生成 Tables 请求的表数据，并防止表名列表与生成代码漂移。</summary>
        private static byte[] GetPreloadedTableBytes(IReadOnlyDictionary<string, byte[]> tableBytes, string tableName)
        {
            if (!tableBytes.TryGetValue(tableName, out byte[] bytes)) throw new InvalidDataException($"Generated Tables requested unregistered binary table '{tableName}'. Update RequiredBinaryTableNames to match generated client tables.");
            return bytes;
        }

        /// <summary>判断 StreamingAssets 是否位于 jar、http 或其他不能通过 System.IO 直接读取的位置。</summary>
        private static bool RequiresUnityWebRequest(string streamingAssetsRoot)
        {
            return streamingAssetsRoot.StartsWith("jar:", StringComparison.OrdinalIgnoreCase) || streamingAssetsRoot.Contains("://");
        }

        /// <summary>拼接 URL 型 StreamingAssets 路径，并统一使用正斜杠避免平台分隔符进入 URI。</summary>
        private static string BuildStreamingAssetUri(string streamingAssetsRoot, string tableName)
        {
            return streamingAssetsRoot.TrimEnd('/', '\\') + "/PromeArchTrial/Config/" + tableName + ".bytes";
        }

        /// <summary>发送 UnityWebRequest 并以取消令牌驱动 Abort，返回完整且非空的表二进制。</summary>
        private static async Task<byte[]> ReadTableBytesWithUnityWebRequestAsync(string uri, CancellationToken cancellationToken)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(uri))
            {
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        request.Abort();
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    await Task.Yield();
                }
                if (request.result != UnityWebRequest.Result.Success) throw new IOException($"Failed to load Luban table binary '{uri}': {request.error}");
                byte[] bytes = request.downloadHandler.data;
                if (bytes == null || bytes.Length == 0) throw new InvalidDataException($"Luban table binary is empty: {uri}.");
                return bytes;
            }
        }
    }
}
