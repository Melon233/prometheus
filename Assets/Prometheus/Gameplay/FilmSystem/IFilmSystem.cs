using System;

namespace Xuan.Prometheus.Film
{
    /// <summary>定义演出播放、恢复、停止和状态观察的公共入口。</summary>
    public interface IFilmSystem : ISystemContract
    {
        /// <summary>当演出生成可持久化快照时触发。</summary>
        event Action<FilmPlaybackSnapshot> SnapshotCaptured;

        /// <summary>获取当前是否存在活动演出。</summary>
        bool IsPlaying { get; }

        /// <summary>获取当前前台演出句柄。</summary>
        FilmHandle ActiveFilm { get; }

        /// <summary>获取当前对话和 QTE 交互端口。</summary>
        IFilmInteractionService InteractionService { get; }

        /// <summary>绑定并启动一段演出。</summary>
        FilmHandle Play(FilmDefinition definition, FilmBindingContext bindings = null, FilmFlowContext flowContext = null);

        /// <summary>从持久化快照恢复一段演出。</summary>
        FilmHandle PlayFromSnapshot(FilmDefinition definition, FilmBindingContext bindings, FilmPlaybackSnapshot snapshot);

        /// <summary>停止当前前台演出。</summary>
        void StopCurrent();
    }
}
