using System;
using System.Collections.Generic;
using System.IO;
using Luban;
using PromeArchTrial.Config;
using PromeArchTrial.Game.Character;
using PromeArchTrial.Game.ConfigAdapter;

namespace PromeArchTrial.BattleServer.Configuration
{
    /// <summary>
    /// 聚合服务器进程唯一的一份 Luban 原始表与由指定 Character 行编译出的不可变纯 C# 运行时配置。
    /// </summary>
    public sealed class BattleServerConfiguration
    {
        private static readonly string[] RequiredBinaryTableNames = { "gameplay_tbbattlerule", "gameplay_tbcharacterproperty", "gameplay_tblocomotion", "gameplay_tbdodge", "gameplay_tbaction", "gameplay_tbactionset", "gameplay_tbcharacter" };

        /// <summary>创建一份已经完成全部 Luban ref 和运行时约束校验的服务器配置。</summary>
        private BattleServerConfiguration(string dataDirectory, int characterId, Tables tables, CharacterRuntimeConfig characterConfig)
        {
            DataDirectory = dataDirectory;
            CharacterId = characterId;
            Tables = tables ?? throw new ArgumentNullException(nameof(tables));
            CharacterConfig = characterConfig ?? throw new ArgumentNullException(nameof(characterConfig));
        }

        /// <summary>获取服务器实际读取的规范化 Luban 二进制目录。</summary>
        public string DataDirectory { get; }

        /// <summary>获取会话默认使用的 Character 表主键，不再存在 rootId 间接选择。</summary>
        public int CharacterId { get; }

        /// <summary>获取已经在构造阶段执行 ResolveRef 的服务端分组表集合。</summary>
        public Tables Tables { get; }

        /// <summary>获取客户端预测和服务器权威模拟共同消费的不可变角色配置。</summary>
        public CharacterRuntimeConfig CharacterConfig { get; }

        /// <summary>获取握手使用的确定性角色配置哈希。</summary>
        public ulong ContentHash => CharacterConfig.ContentHash;

        /// <summary>从 Luban cs-bin 目录加载服务端分组表，自动 ResolveRef，并直接按照 Character ID 编译共享配置。</summary>
        public static BattleServerConfiguration Load(string dataDirectory, int characterId)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("Luban data directory is required.", nameof(dataDirectory));
            if (characterId <= 0) throw new ArgumentOutOfRangeException(nameof(characterId), "Character ID must be positive.");
            string normalizedDirectory = Path.GetFullPath(dataDirectory);
            if (!Directory.Exists(normalizedDirectory)) throw new DirectoryNotFoundException($"Server Luban data directory does not exist: {normalizedDirectory}. Run Prometheus/Configs/Luban/generate-server.bat first.");
            Dictionary<string, byte[]> tableBytes = new Dictionary<string, byte[]>(RequiredBinaryTableNames.Length, StringComparer.Ordinal);
            for (int index = 0; index < RequiredBinaryTableNames.Length; index++)
            {
                string tableName = RequiredBinaryTableNames[index];
                string filePath = Path.Combine(normalizedDirectory, tableName + ".bytes");
                if (!File.Exists(filePath)) throw new FileNotFoundException($"Required server Luban table does not exist: {filePath}. Run Prometheus/Configs/Luban/generate-server.bat first.", filePath);
                byte[] bytes = File.ReadAllBytes(filePath);
                if (bytes.Length == 0) throw new InvalidDataException($"Server Luban table is empty: {filePath}.");
                tableBytes.Add(tableName, bytes);
            }
            Tables tables = new Tables(tableName => new ByteBuf(GetPreloadedTableBytes(tableBytes, tableName)));
            CharacterRuntimeConfig characterConfig = CharacterLubanConfigAdapter.Compile(tables, characterId);
            return new BattleServerConfiguration(normalizedDirectory, characterId, tables, characterConfig);
        }

        /// <summary>从预加载字典取得生成 Tables 请求的数据，并在代码与服务端分组文件列表漂移时立即失败。</summary>
        private static byte[] GetPreloadedTableBytes(IReadOnlyDictionary<string, byte[]> tableBytes, string tableName)
        {
            if (!tableBytes.TryGetValue(tableName, out byte[] bytes)) throw new InvalidDataException($"Generated server Tables requested unregistered binary table '{tableName}'. Regenerate the server group and update RequiredBinaryTableNames together.");
            return bytes;
        }
    }
}
