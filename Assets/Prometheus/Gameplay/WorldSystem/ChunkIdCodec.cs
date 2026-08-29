using UnityEngine;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// chunkId 编解码：三位编码 chunkX*1000 + chunkY，chunk 坐标从 0 起且非负。
    /// 客户端编辑时只向正方向添加 chunk，故无需负坐标偏移。
    /// </summary>
    public static class ChunkIdCodec
    {
        /// <summary>单个 chunk 的世界边长（米），与 AOI RegionSize 一致。</summary>
        public const int ChunkSize = 20;

        /// <summary>三位编码基数。</summary>
        public const int ChunkBase = 1000;

        /// <summary>由 chunk 坐标 (cx, cy) 编码为 chunkId。</summary>
        public static int Encode(int chunkX, int chunkY) => chunkX * ChunkBase + chunkY;

        /// <summary>由世界坐标计算所属 chunkId（x→chunkX，z→chunkY）。</summary>
        public static int EncodeFromPosition(Vector3 position) => Encode(FloorToChunk(position.x), FloorToChunk(position.z));

        /// <summary>从 chunkId 解码出 chunkX。</summary>
        public static int ChunkX(int chunkId) => chunkId / ChunkBase;

        /// <summary>从 chunkId 解码出 chunkY。</summary>
        public static int ChunkY(int chunkId) => chunkId % ChunkBase;

        /// <summary>世界坐标向下取整到 chunk 坐标。</summary>
        private static int FloorToChunk(float value) => Mathf.FloorToInt(value / ChunkSize);
    }
}
